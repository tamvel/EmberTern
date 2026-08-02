# EmberTern — Język kolorów

> **Status: PROJEKT DO AKCEPTACJI (2026-08-02).** Nic z tego dokumentu nie jest jeszcze wdrożone.
> Powstał na polecenie użytkownika po wycofaniu M3.2b, w odwróconej kolejności: **pomiar → projekt →
> akceptacja → dopiero implementacja.**
>
> **To NIE jest lista zmian dla jednego etapu.** To dokument produktu — ma służyć przy każdej nowej
> funkcji, długo po zamknięciu Product Polish. Lista zmian wynika z niego, a nie odwrotnie.
>
> **Wejścia:** pomiar całego produktu (`product-polish.md` §20) · trzy ratyfikowane odpowiedzi
> użytkownika (§0.2) · `product-polish.md` §7.5 jako **jedno z wejść, nie źródło dedukcji** ·
> lekcja z §19.14 (pułapka 17).

---

## §0 Po co ten dokument istnieje

### §0.1 ⭐⭐ Problem, który rozwiązuje — zmierzony, nie założony

> **Problemem EmberTerna nie jest liczba kolorów, tylko ich NIESTAŁOŚĆ.**

Pomiar całego produktu (§20): **442 ikony w widokach, z czego 39 kolorowych — 91 % aplikacji jest już
neutralne.** Aplikacja nie jest przekolorowana. Defekt polega na czym innym: **dziewięć akcji zmienia
kolor zależnie od modułu, w którym stoi.** „Uruchom" ma cztery kolory, „usuń trwale" trzy, „odśwież"
trzy, „edytuj" trzy.

⛔ **Dlatego celem NIE jest ograniczenie liczby kolorów.** Cel to: *ta sama akcja wygląda tak samo
w całym produkcie*. Kolor ma **pomagać rozpoznać akcję, zanim się ją przeczyta** — a robi to tylko
wtedy, gdy jest przewidywalny.

### §0.2 ⭐ Ratyfikowane wejścia — nie podlegają ponownemu otwarciu

| # | Decyzja użytkownika (2026-08-02) |
|---|---|
| **W1** | **Ta sama akcja → ten sam kolor w całej aplikacji.** To jest reguła nadrzędna tego dokumentu |
| **W2** | **Różne akcje, które łatwo pomylić, MOGĄ świadomie mieć różne kolory**, jeśli realnie poprawia to rozpoznawalność. *„Jeśli wyjątek poprawia UX, to jest świadomym wyjątkiem, a nie błędem do usunięcia"* |
| **W3** | ⛔ **Nie budujemy systemu „im mniej kolorów, tym lepiej"** |
| **W4** | **Execute i Commit to DWIE role** → osobne tokeny, **na razie ten sam odcień** zieleni. Rozdzielenie w przyszłości ma nie wymagać ruszania drugiego |
| **W5** | **🟡 żółty = ostrzeżenie · stan wymagający uwagi · wstrzymanie. NIGDY destrukcja.** Usuwanie jest konsekwentnie czerwone |
| **W6** | **Moduły mogą mieć tożsamość kolorystyczną, ale tylko wewnątrz modułu.** ⭐ Wyjątek: w pasku globalnym **wolno** użyć koloru modułu, gdy element niesie **STAN** („który moduł jest aktywny?"), a nie akcję |

### §0.3 ⛔ Reguła metody — dlaczego ten dokument ma sekcję wyjątków

Cztery poprzednie iteracje odrzucono, bo **regułę doprowadzano do logicznej konsekwencji zamiast
spojrzeć na aplikację** (`product-polish.md` §19.14, pułapka 17):

> **Reguła opisuje to, co już jest dobre; nie jest mandatem do zmiany wszystkiego, co do niej nie pasuje.
> Element niezgodny z regułą bywa wyjątkiem, który DZIAŁA.**

Dlatego §5 (wyjątki) jest **częścią języka na równi z §3** (role), a nie listą długu. Wyjątek nazwany
i uzasadniony jest poprawnym stanem docelowym.

---

## §1 ⭐⭐ CZTERY NIEZALEŻNE SYSTEMY — serce projektu

Największy błąd poprzednich podejść polegał na traktowaniu koloru jako **jednej** skali
(„success / danger / neutral"). W IDE kolor odpowiada na **cztery różne pytania**, w rozłącznych
kontekstach — dlatego nie są ze sobą sprzeczne i użytkownik ich nie myli.

| System | Pytanie | Nośnik | Gdzie obowiązuje |
|---|---|---|---|
| **S1 · RODZAJ** | *„czego to dotyczy?"* | `IconColor_*` (10 tokenów) | drzewo metadanych, zakładki, kreatory, nagłówki edytorów, menu |
| **S2 · AKCJA** | *„co to zrobi?"* | role z §3 | **każdy przycisk akcji w całym produkcie** |
| **S3 · TOŻSAMOŚĆ MODUŁU** | *„który moduł jest aktywny?"* | palety modułów (§4) | ⚠ **wewnątrz modułu** + chipy **stanu** w pasku globalnym (W6) |
| **S4 · HIERARCHIA PRZYCISKU** | *„czy to akcja główna?"* | wariant `Button.primary` + `OnAccentBrush` | dowolna powierzchnia |

### §1.1 ⚠⚠ S4 nie jest kolorem semantycznym i musi być rozdzielony od S2

`OnAccentBrush` na ikonie **nie znaczy nic o akcji** — to biel zapewniająca kontrast na wypełnieniu
akcentem. Kolor niesie tam **wariant przycisku**, nie ikona (decyzja architektoniczna 4 M2b: *wariant
niesie kolor, kontekst niesie rozmiar*).

⭐ **Bez tego rozdzielenia Execute w SQL Editorze (`primary`, biała ikona) i Execute procedury
(`icon`, zielona ikona) są nieporównywalne** — a to właśnie ta pozorna sprzeczność wygenerowała
„4 kolory akcji Uruchom" w pomiarze. **Realnie są to dwa różne WARIANTY tej samej roli**, nie dwa
kolory. Zapis w §3.1.

### §1.2 Kolejność rozstrzygania, gdy systemy się spotkają

1. **S4 wygrywa geometrią**: jeśli akcja jest na `Button.primary`, jej ikona jest `OnAccentBrush`,
   a rolę S2 niesie **kolor wypełnienia przycisku**.
2. **S2 wygrywa z S3**: przycisk *wykonujący akcję* w pasku globalnym używa języka akcji, nawet jeśli
   należy do modułu (W6).
3. **S2 wygrywa z S1**: gdyby element miał nieść i rodzaj, i skutek — wygrywa skutek. Ostrzeżenie
   o nieodwracalności jest ważniejsze niż informacja o typie.
4. **S3 obowiązuje w pasku globalnym tylko dla STANU** — chip, nie przycisk.

---

## §2 Zakres — czego język NIE dotyczy

| Powierzchnia | Dlaczego poza |
|---|---|
| **Menu kontekstowe** (131 pozycji) | ⭐ **Osobny, JUŻ SPÓJNY system** z etapu 5 Keyboard Managera: neutralnie, wyjątkiem jest destrukcja (13 pozycji `Brush=DangerIconBrush`). ⛔ Nie ujednolicać z przyciskami |
| **Paleta składni edytora** | zamrożona (§6.3 Product Polish) |
| **Kolory rodzajów obiektów** (`IconColor_*`) | działają, są znaczące, dają produktowi charakter — S1 zostaje bez zmian |
| **Kolory stanu w siatkach i panelach** | (wiersz zmieniony, wynik testu, severity `MessageBanner`) — to komunikaty, nie akcje |

---

## §3 ⭐⭐ KATALOG RÓL AKCJI (system S2)

> Każdy przycisk akcji w produkcie ma **dokładnie jedną** z poniższych ról.

| # | Rola | Znaczenie | Token | Obejmuje |
|---|---|---|---|---|
| **R‑1** | **Uruchom** 🟢 | wykonanie kodu lub operacji na danych | `ActionRunBrush` *(nowy, = odcień `Success`)* | Execute SQL · Execute procedury · Execute funkcji · Run script · Run import · Start trace |
| **R‑2** | **Zatwierdź** 🟢 | zatwierdzenie transakcji | `CommitButtonBrush` *(istnieje, do dostrojenia)* | Commit — toolbar, Script Executor, Data Import, Session Manager |
| **R‑3** | **Wycofaj transakcję** 🔴 | wycofanie **zatwierdzalnej** pracy | `RollbackButtonBrush` *(istnieje, do dostrojenia)* | Rollback — toolbar, Script Executor, Data Import |
| **R‑4** | **Destrukcja / Zatrzymanie** 🔴 | operacja nieodwracalna albo przerwanie wykonania | `DangerIconBrush` | Usuń · Upuść · Wyczyść wszystko · Stop · Anuluj wykonanie · rozłącz **cudzą** sesję |
| **R‑5** | **Ostrzeżenie / Wstrzymanie** 🟡 | stan wymagający uwagi, pauza, tryb warunkowy | `WarningIconBrush` | Pause (Trace) · Break on exception (debugger) |
| **R‑6** | **Wejście do narzędzia / wymiana z zewnętrzem** 🔵 | otwarcie modułu albo operacja plikowa | `AccentBrush` | Global Search · Script Executor · Data Import · Activity Monitor · Session Manager · Export DDL · Open/Save script · wskaż plik źródłowy |
| **R‑7** | **Narzędzie** ⚪ | wszystko, co działa na bieżącym dokumencie i nie ma skutku z R‑1…R‑5 | **brak `Foreground`** → `NeutralIconBrush` | Odśwież · Edytuj · Dodaj · Usuń pozycję z bufora · Format · Kopiuj · Szukaj w widoku · Revert · przewijanie · przełączniki trybu |

### §3.1 ⚠ Każda rola ma dwa warianty — i to nie są dwa kolory

| Wariant | Kiedy | Jak wygląda |
|---|---|---|
| **ikonowy** | przycisk `Classes="icon"` w chromie | ikona nosi token roli |
| **główny** | przycisk `Classes="primary"` | **wypełnienie** niesie akcent, ikona jest `OnAccentBrush` |

⭐ Execute w SQL Editorze i Execute procedury mają **tę samą rolę R‑1** i różnią się wyłącznie
wariantem. ⛔ Nie „ujednolicać" ich koloru ikony — to by znaczyło pomalować białą ikonę na zielono
na niebieskim tle.

### §3.2 ⚠ `Revert` NIE jest `Rollback` — dwie różne role

| | Akcja | Rola |
|---|---|---|
| **Rollback** | wycofuje **transakcję bazy danych** | **R‑3** 🔴 |
| **Revert / Discard** | porzuca **niezapisany bufor edytora** | **R‑7** ⚪ |

Obie używają `Icon.Undo` i to jest w porządku — **ikona nie jest nośnikiem roli, token nim jest.**
⚠ Rollback traci pracę **zatwierdzalną**; Revert traci pracę, która nigdy nie opuściła edytora.

---

## §4 System S3 — tożsamość modułów

| Moduł | Paleta | Istnieje dziś? |
|---|---|---|
| **Debugger** | `DebugCurrentLineBarBrush` · `DebugBreakpointBrush` · `DebugLoopIconBrush` · `DebugParamIn/Out/Local` · `DebugPinBrush` | ✅ pełna, spójna |
| **Trace / monitoring** | `IconColor_Query` (rail chipa, M3.1e) | ⚠ tylko rail; **fioletu z Twojego szkicu nie ma** |

⚠⚠ **Ograniczenie S3 (W6):** paleta modułu maluje **stan i elementy własnej powierzchni** — nigdy
przycisku akcji. Chip Debug/Trace w pasku statusu jest **poprawnym** użyciem, bo odpowiada na
*„który moduł jest aktywny?"*. Przycisk „Otwórz Activity Monitor" w pasku tytułu — **nie**; to akcja
i należy do R‑6.

---

## §5 ⭐⭐ WYJĄTKI NAZWANE — część języka, nie dług

> ⛔ Każdy z poniższych był albo **zamówiony przez użytkownika**, albo **wynika z różnicy znaczeń**.
> Kolejna iteracja nie ma ich „naprawiać". Wyjątek bez wpisu tutaj jest defektem; wyjątek z wpisem —
> stanem docelowym.

| # | Wyjątek | Powód |
|---|---|---|
| **W‑1** | **Comment / Uncomment mają różne kolory** | ⛔ **Zamówione przez użytkownika**: ikony są bardzo podobne, a kolor pozwala je rozpoznać błyskawicznie. Realizacja W2. ⚠ M3.2b uznało to za defekt i **to był błąd** |
| **W‑2** | **Rollback 🔴 vs Revert ⚪** | dwie różne role (§3.2), nie dwa kolory jednej |
| **W‑3** | **Rozłącz cudzą sesję 🔴 vs własne połączenie ⚪** | rozłączenie cudzej sesji dotyka pracy innego użytkownika i jest dla niego nieodwracalne |
| **W‑4** | **Ikona `OnAccentBrush` na `primary`** | kontrast, nie semantyka (§1.1) |
| **W‑5** | **Chip Debug/Trace w pasku statusu nosi kolor modułu** | niesie **stan**, nie akcję (W6) |
| **W‑6** | **Kompiluj zawsze na `primary`** (11/11) | to akcja główna każdego edytora obiektu — hierarchia, nie kolor |

---

## §6 ⭐ Reguła rozstrzygająca — jak pokolorować NOWĄ akcję

> Zadaj pytania **po kolei** i zatrzymaj się na pierwszym „tak".

1. **Czy to przycisk akcji?** Nie → to nie jest S2 (menu · chip stanu · ikona rodzaju · komunikat).
2. **Czy stoi na `Button.primary`?** Tak → ikona `OnAccentBrush`, rolę niesie wypełnienie. Koniec.
3. **Czy wykonuje kod lub operację na danych?** → **R‑1** 🟢
4. **Czy zatwierdza transakcję?** → **R‑2** 🟢  ·  **Czy ją wycofuje?** → **R‑3** 🔴
5. **Czy jest nieodwracalna albo przerywa wykonanie?** → **R‑4** 🔴
6. **Czy sygnalizuje ostrzeżenie, pauzę lub tryb warunkowy?** → **R‑5** 🟡
7. **Czy otwiera moduł albo wymienia dane z zewnętrzem (plik)?** → **R‑6** 🔵
8. **W pozostałych przypadkach** → **R‑7** ⚪ (brak `Foreground`).

### §6.1 ⚠ Dwa pytania kontrolne, ZANIM zmienisz istniejący przycisk

Wynikają wprost z czterech odrzuconych iteracji (pułapka 17):

1. **Czy ten element jest niezgodny, bo to błąd — czy dlatego, że ktoś świadomie tak chciał?**
   Sprawdź §5 i historię. Brak wpisu w §5 **nie dowodzi**, że wyjątku nie było — dowodzi, że nie
   został zapisany; wtedy zapytaj, nie zmieniaj.
2. **Co użytkownik traci, jeśli się mylę?** Jeśli odpowiedź brzmi *„rozpoznawalność"* — nie zmieniaj
   bez obejrzenia na żywo.

### §6.2 ⛔ Czego reguła NIE upoważnia

* ⛔ Nie upoważnia do **odbarwiania** elementu tylko dlatego, że nie mieści się w R‑1…R‑6. Sprawdź
  najpierw §5 i §6.1.
* ⛔ Nie upoważnia do **kolorowania** narzędzia tylko po to, żeby „miało rolę". R‑7 jest pełnoprawną
  odpowiedzią i obejmuje **91 % ikon produktu**.

---

## §7 Tokeny — stan, praca do wykonania, decyzje

### §7.1 Istnieją i pasują bez zmian

`DangerIconBrush` · `WarningIconBrush` · `AccentBrush` · `NeutralIconBrush` · `OnAccentBrush` ·
10 × `IconColor_*` · paleta debuggera.

### §7.2 ⚠⚠ Istnieją, ale wymagają dostrojenia PRZED użyciem

`CommitButtonBrush` i `RollbackButtonBrush` **nie mają dziś ani jednego konsumenta** — i to nie jest
jedyny problem. Zmierzone:

| Token | Dark | Light | Uwaga |
|---|---|---|---|
| `CommitButtonForeground` | `#4CAF50` | `#4CAF50` | ⚠ **identyczne w obu motywach** |
| `RollbackButtonForeground` | `#F44336` | `#F44336` | ⚠ **identyczne w obu motywach** |
| `SuccessIconColor` (dla porównania) | `#6DBE7E` | `#2E8B4F` | dostrojone per motyw |
| `DangerIconColor` (dla porównania) | `#D77373` | `#C23B3B` | dostrojone per motyw |

⭐⭐ **To są wartości Material Design wstawione „na zapas", nigdy nieużyte i nigdy niedostrojone do
palety EmberTerna.** Każdy inny token semantyczny ma osobną wartość dla Light i Dark — bo ten sam
odcień nie może mieć poprawnego kontrastu na obu tłach. **Przyjęcie ich bez dostrojenia wprowadziłoby
regres kontrastu w motywie jasnym** (dokładnie klasa problemu V‑1 i §10).

⛔ **Wniosek dla implementacji: decyzja DD (Commit/Rollback przechodzą na własne tokeny) obowiązuje,
ale jej wykonanie zaczyna się od nadania tym tokenom wartości per motyw, a nie od podmiany
odwołań.**

### §7.3 Do dodania

| Token | Rola | Wartość |
|---|---|---|
| `ActionRunBrush` | **R‑1 Uruchom** | ⭐ **na razie równy `SuccessIconColor`** (`#6DBE7E` / `#2E8B4F`) — realizacja W4: osobny token, ten sam odcień, rozdzielenie możliwe później bez ruszania Commita |

### §7.4 ⚠ Duplikat do rozstrzygnięcia

`WarningBrush` (`#E8A020`/`#C77800`) i `WarningIconBrush` (`#DBA13C`/`#B5790E`) — **dwie nazwy na jedną
rolę**, używane zamiennie (`Icon.Exception`, Pause → `WarningBrush`; Break on exception, Edytuj, Usuń
połączenie → `WarningIconBrush`). Język używa **`WarningIconBrush`** dla R‑5; `WarningBrush` zostaje
dla **tekstu i komunikatów**, gdzie ma innych konsumentów.

### §7.5 ⏸ Nie w tym języku — brak konsumenta

**Fiolet dla Trace** z Twojego szkicu **nie istnieje jako token**, a Trace ma dziś tylko rail
(`IconColor_Query`). ⚠ Zgodnie z R3 (*rola powstaje z użycia w kilku komponentach*) nie dodaję go
teraz na zapas — wchodzi wtedy, gdy Trace dostanie własną powierzchnię wymagającą tożsamości.

---

## §8 Co ten język ZMIENIA, a co OPISUJE

⭐ **Miara jakości projektu: ile już działa.** Pięć akcji jest wzorcowo spójnych i język je **utrwala**,
nie zmienia.

### §8.1 Opisuje stan istniejący (zero pracy)

| Rola | Dowód |
|---|---|
| R‑4 Stop | **5/5** już `DangerIconBrush` |
| R‑2 Commit (kolor zielony) | **5/5** już zielony |
| R‑7 Narzędzie | **91 %** ikon już neutralne |
| R‑6 Wejście do narzędzia | 6/8 już `AccentBrush` |
| S1 Rodzaj · menu kontekstowe · `primary` | 10/10 · 131 pozycji · 11/11 |

### §8.2 Wymaga zmiany — 9 akcji z §20.2

| Akcja | Dziś | Wg języka |
|---|---|---|
| Usuń połączenie · Usuń zapytanie · Wyczyść zapytania | 🟡 `Warning` | **R‑4** 🔴 — realizacja W5 |
| Execute procedury / funkcji / Start trace | 🟢 `Success` | **R‑1** `ActionRunBrush` (ten sam odcień ⇒ **bez zmiany wizualnej**) |
| Odśwież (metadane, dane tabeli, import) | 🔵 `Info` / `AccentIcon` | **R‑7** ⚪ |
| Edytuj (Procedure, Function, profil importu) | 🟡 `Warning` / 🔵 `AccentIcon` | **R‑7** ⚪ |
| Dodaj (Procedure, Function) | 🟢 `Success` | **R‑7** ⚪ |
| Szukaj w widoku (Trace) | `Subtle` | **R‑7** ⚪ |
| Wskaż plik (Data Import) | 🔵 `AccentIcon` | **R‑6** `AccentBrush` |
| Connect (pasek tytułu) | 🔵 `AccentIcon` | **R‑7** ⚪ *(nie otwiera modułu, działa na zaznaczeniu)* |
| Commit · Rollback | `Success` / `Danger` | **R‑2 / R‑3** — po dostrojeniu §7.2 |

⚠⚠ **Zwróć uwagę, co się NIE zmienia: sześć narzędzi w pasku tytułu zostaje kolorowych** (R‑6), bo
otwierają moduły. To jest różnica względem M3.2b, które je odbarwiło — i powód, dla którego tamta
iteracja czytała się jako wyszarzenie.

---

## §9 ⛔ Otwarte — wymaga decyzji przed implementacją

| # | Pytanie | Kontekst |
|---|---|---|
| **O‑1** | **Debugger Continue** — dziś `AccentIconBrush`, ratyfikowany w **D15.2 Seam A** jako „jedyna akcja pierwszorzędna debuggera". Wg R‑1 powinien być zielony. **Kolizja dwóch ratyfikacji** — nie rozstrzygam sam | zmiana dotknęłaby powierzchni odebranej wizualnie |
| **O‑2** | **Comment / Uncomment — jakie dwa kolory?** Dziś `Info` + `Danger`. Rozróżnienie zostaje (W‑1), ale **`Danger` na Uncomment osłabia jednoznaczność czerwieni** („nieodwracalne"), a odkomentowanie cofa się jednym Ctrl+Z | opcje w §9.1 |
| **O‑3** | **Zakres pierwszej implementacji** — całość §8.2 naraz, czy podetapami | całość to ~20 miejsc w 6 plikach |

### §9.1 Opcje dla O‑2

| Wariant | Comment | Uncomment | Koszt |
|---|---|---|---|
| **(a) bez zmian** | `Info` 🔵 | `Danger` 🔴 | zero pracy; ⚠ czerwony ma dwa znaczenia |
| **(b) para poza systemem skutku** ⭐ | `Info` 🔵 | `WarningIconBrush` 🟡 | ⚠ łamie W5 (żółty = ostrzeżenie), więc raczej nie |
| **(c) para w obrębie jednego odcienia** ⭐⭐ | `InfoIconBrush` 🔵 | `AccentIconBrush` 🔵 ciemniejszy | zachowuje rozróżnienie, **zwalnia czerwień**, nie wchodzi w rolę ostrzeżenia |

---

## §10 Jak stosować przy nowej funkcji — skrót

1. Ikona bierze rolę z **§6**, nie z modułu, w którym stoi.
2. Nie ma roli → **R‑7**, czyli **brak `Foreground`**. To poprawna odpowiedź, nie brak decyzji.
3. Ta sama akcja gdzie indziej → **ten sam token** (W1). Jeśli musisz odejść — dopisz wyjątek do §5
   **z powodem**, zanim to zrobisz.
4. Neutralny dla **ikony** to `NeutralIconBrush` (brak `Foreground`), **nie `ForegroundBrush`** —
   to dwa różne tokeny i różnica jest celowa (`product-polish.md` §19.13.3).
5. Nowy kolor tylko wtedy, gdy ma **kilku konsumentów** (R3). Jeden przypadek to wyjątek, nie rola.
