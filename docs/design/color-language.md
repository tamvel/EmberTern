# EmberTern — Język kolorów

> **Status: 🔒 WDROŻONY W CAŁOŚCI I ODEBRANY WIZUALNIE (2026-08-03). Zero otwartych pytań.**
> Projekt zaakceptowany 2026-08-02, wdrożony krokami K1–K7 + przeglądem domykającym; zapis wykonania:
> `product-polish.md` **§19.15–§19.19**.
>
> ⭐⭐ **OD TEJ CHWILI TO JEST DOKUMENT REFERENCYJNY, NIE PLAN.** Służy przy **każdej nowej funkcji**
> — dodajesz przycisk, bierzesz mu rolę z **§6**. Nie ma tu już nic do „dokończenia".
>
> ⛔⛔ **PRZED ZMIANĄ JAKIEGOKOLWIEK KOLORU PRZECZYTAJ §0.5** — bramka nadrzędna („czy użytkownik
> rozpozna akcję SZYBCIEJ?"), która stoi **ponad** regułą ról z §6 i ponad tempem z §0.4.
>
> **Wejścia (historyczne):** pomiar całego produktu (`product-polish.md` §20) · ratyfikowane odpowiedzi
> użytkownika (§0.2) · `product-polish.md` §7.5 jako **jedno z wejść, nie źródło dedukcji** ·
> lekcja z §19.14 (pułapka 17).
>
> ⚠ **§0.4 (R14, tempo krok‑po‑kroku) jest ZAMKNIĘTE i historyczne** — obowiązywało, dopóki język był
> projektowany. Aktualną regułę tempa niesie **R15** (handover §5): *wielkość iteracji idzie za
> niepewnością.*

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

### §0.4 🔒 R14 — TEMPO WDROŻENIA (ratyfikowane 2026-08-02, **ZAMKNIĘTE 2026-08-03**)

> 🔒 **HISTORYCZNE.** Obowiązywało, dopóki język był PROJEKTOWANY — i wtedy było słuszne: kilka razy
> poprawna reguła dawała gorszy ekran. Po akceptacji dokumentu użytkownik sam je zdjął: *„nie chcę
> dalej pracować w tak drobnych iteracjach, zaczyna nas to bardziej spowalniać niż pomagać"*, i K2–K7
> poszły jednym przebiegiem. ⭐ Uogólnienie tej pary decyzji to **R15** (handover §5): **wielkość
> iteracji idzie za NIEPEWNOŚCIĄ, nie za ostrożnością.**
>
> ⚠ Treść R14 nie zniknęła — żyje dalej jako **§0.5**, tylko przeniesiona z *tempa* na *kryterium*.

> **Użytkownik:** *„Chcę jednak, żeby implementacja nadal była bardzo ostrożna. Nie zależy mi na tym,
> żeby w jednej iteracji ujednolicić cały produkt. Bardziej zależy mi na tym, żeby każdy kolejny krok
> był ewidentnym ulepszeniem UX i nie powtórzył sytuacji z pierwszym M3.2b, gdzie reguły były
> spójniejsze, ale produkt wyglądał gorzej. **Wolę pięć małych, oczywistych poprawek niż jedną dużą
> rewolucję.**"*

**R14 — kryterium pojedynczego kroku:**

| | |
|---|---|
| ✅ **Krok jest dobry, gdy** | jest **ewidentnym ulepszeniem UX sam w sobie** — da się go obejrzeć i powiedzieć „tak, lepiej", bez odwoływania się do reguły |
| ⛔ **Krok jest zły, gdy** | jego jedynym uzasadnieniem jest *„teraz jest zgodne z językiem"* |

⭐ **To jest R8 zastosowane do TEMPA, a nie do pojedynczej decyzji**, i domyka pułapkę 17 od strony
praktycznej: reguła spójna + zmiana niewidoczna dla użytkownika = zmiana, która może tylko zaszkodzić,
bo ryzyko jest niezerowe, a zysk zerowy.

⚠ **Konsekwencja dla kolejności prac: §8.2 NIE jest listą jednej iteracji.** Rozstrzyga to pytanie
**O‑3** — implementacja idzie **podetapami, posortowanymi od najbardziej oczywistego zysku**, a każdy
kończy się obejrzeniem na żywo. Kolejność w §11.

### §0.5 ⛔⛔ PYTANIE NADRZĘDNE — obowiązuje przed KAŻDĄ zmianą koloru (ratyfikowane 2026-08-03)

> **Użytkownik:** *„Zanim zmienisz jakikolwiek kolor, zawsze odpowiedz sobie na pytanie: czy użytkownik
> dzięki tej zmianie szybciej rozpozna akcję. Jeżeli odpowiedź brzmi «nie» albo «nie wiadomo»,
> zatrzymaj się i wróć z propozycją zamiast implementować. To ma być nadrzędna zasada dla całego
> wdrażania."*

| Odpowiedź | Co robisz |
|---|---|
| **TAK**, i potrafisz powiedzieć **dlaczego** | implementujesz |
| **NIE** | ⛔ nie zmieniasz — kolor już działa |
| **NIE WIADOMO** | ⛔ **zatrzymujesz się i wracasz z propozycją.** „Nie wiadomo" jest odpowiedzią odmowną, nie zaproszeniem do spróbowania |

⭐ **To jest bramka MOCNIEJSZA niż §6 i niż R14, i stoi przed nimi.** §6 mówi, *jaki* kolor przysługuje
roli; R14 mówi, że krok ma być ewidentnym ulepszeniem. Ta zasada mówi, **czym mierzy się „ulepszenie":
szybkością rozpoznania akcji przez człowieka**, a nie zgodnością z tabelą. Zgodność z rolą jest
warunkiem koniecznym, nigdy wystarczającym.

⚠ **Konsekwencja praktyczna:** zdanie *„teraz jest zgodne z językiem"* **nie jest odpowiedzią na to
pytanie**. To właśnie ono uzasadniało M3.2b (§19.14) — i było jedynym uzasadnieniem, jakie tamta
iteracja miała.

⭐ Cztery zasady towarzyszące, ratyfikowane tym samym głosem: nie wdrażamy reguł mechanicznie · każda
zmiana ma poprawiać **odbiór produktu** · po każdej iteracji **QA wizualne** · ⭐⭐ **jeśli podczas
implementacji okaże się, że reguła pogarsza UX — wracamy do TEGO dokumentu i poprawiamy regułę, a nie
bronimy implementacji.**

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
| **W‑1** | **Comment / Uncomment mają różne kolory** — ⭐ od 2026-08-03 **🔵 `InfoIconBrush` vs ⚪ neutralny** | ⛔ **Zamówione przez użytkownika**: ikony są bardzo podobne, a kolor pozwala je rozpoznać błyskawicznie. Realizacja W2. ⚠ M3.2b uznało to za defekt i **to był błąd**. ⚠⚠ Wyjątek **zostaje**, zmieniła się tylko para kolorów: `DangerIconBrush` na odkomentowaniu obiecywał nieodwracalność akcji cofanej jednym Ctrl+Z i osłabiał czerwień dokładnie tam, gdzie K2 ją zbudowało (O‑2) |
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
| ✅ `ActionRunBrush` | **R‑1 Uruchom** | ⭐ **na razie równy `SuccessIconColor`** (`#6DBE7E` / `#2E8B4F`) — realizacja W4: osobny token, ten sam odcień, rozdzielenie możliwe później bez ruszania Commita |

⭐ **WDROŻONY w kroku K1 (2026-08-03)** — jako **własny `ActionRunColor`, nie alias** nad
`SuccessIconColor`. Alias znaczyłby *„Uruchom to jest kolor sukcesu"*, więc przestrojenie zieleni
Commita przesuwałoby po cichu także Execute — czyli to samo zlanie ról, które W4 kończy. Zapis
i pomiar odbiorczy: `product-polish.md` **§19.15**.

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
| ~~Edytuj (Procedure, Function)~~, profil importu | 🟡 `Warning` / 🔵 `AccentIcon` | **R‑7** ⚪ — ⚠⚠ **KOREKTA 2026-08-03: tylko profil importu.** Procedure/Function to wiersze `UpdateChange` w karcie podsumowania zmian — **stan, nie akcja** (§2) |
| ~~Dodaj (Procedure, Function)~~ | 🟢 `Success` | ⛔ **WYCOFANE 2026-08-03** — to te same wiersze stanu (`InsertChange`), nie przyciski |
| ~~Szukaj w widoku (Trace)~~ | `Subtle` | ⛔ **WYCOFANE 2026-08-03** — to **glif w polu tekstowym**, nie przycisk; pociemnienie zrobiłoby z dekoracji element głośniejszy od treści |
| Wskaż plik (Data Import) | 🔵 `AccentIcon` | **R‑6** `AccentBrush` |
| Connect (pasek tytułu) | 🔵 `AccentIcon` | ⏸ **OTWARTE — NIE WYKONANE, patrz §9 / O‑4.** Reguła mówi R‑7, ale §0.5 odpowiada „nie wiadomo" |
| Commit · Rollback | `Success` / `Danger` | **R‑2 / R‑3** — po dostrojeniu §7.2 |

⚠⚠ **Trzy z dziewięciu wierszy tej tabeli nie przetrwały dokładniejszego pomiaru (2026-08-03), i to jest
wynik, a nie brak.** Inwentarz §20 zliczał `SvgIcon` po tokenie, więc **glif STANU w karcie i glif
w polu tekstowym trafiły do tabeli AKCJI**. ⭐ Lekcja ogólna: *pomiar po nośniku (ikona + token) nie
odróżnia roli od stanu — to rozróżnienie robi dopiero kontekst, w którym element stoi.* Konsekwencja
dla następnych prac: **§2 (kolory stanu poza językiem) jest filtrem, przez który trzeba przepuścić
każdą pozycję inwentarza, zanim uzna się ją za akcję.**

⚠⚠ **Zwróć uwagę, co się NIE zmienia: sześć narzędzi w pasku tytułu zostaje kolorowych** (R‑6), bo
otwierają moduły. To jest różnica względem M3.2b, które je odbarwiło — i powód, dla którego tamta
iteracja czytała się jako wyszarzenie.

---

## §9 ⛔ Otwarte — wymaga decyzji przed implementacją

> ⭐⭐ **WSZYSTKIE PYTANIA ZAMKNIĘTE 2026-08-03** w przeglądzie domykającym (`product-polish.md` §19.18),
> na polecenie użytkownika: *„Chcę jeszcze raz przejrzeć całość pod kątem spójności produktu, a nie
> tylko zgodności z dokumentem… jeśli wyjątek jest tylko pozostałością po starym stanie aplikacji,
> po prostu go doprowadź do zgodności."*

| # | Pytanie | Rozstrzygnięcie |
|---|---|---|
| ~~**O‑1**~~ | ~~Debugger Continue~~ | ✅ **ZAMKNIĘTE → `ActionRunBrush`.** ⭐ Kolizja dwóch ratyfikacji była pozorna: D15.2 chciało **wyróżnienia**, a nie akurat niebieskiego — ten wybrano, gdy token roli „Uruchom" jeszcze nie istniał. Continue nadal jest jedynym wyróżnionym przyciskiem tego paska. ⚠ Dodatkowo niebieski jest w debuggerze kolorem **tożsamości modułu**, a W6 zabrania malować nim przycisk akcji |
| ~~**O‑2**~~ | ~~Comment / Uncomment~~ | ✅ **ZAMKNIĘTE → Comment 🔵 `InfoIconBrush`, Uncomment ⚪.** ⭐ **W‑1 zostaje w mocy** — rozróżnienie kolorem było zamówione i nadal działa; zmienia się tylko to, KTÓRYM kolorem. ⚠⚠ **Wariant (c) z §9.1 zmierzony i ODRZUCONY:** `InfoIconColor` `#5BA7D0` i `AccentIconColor` `#5B9BD5` różnią się o 12 jednostek na jednym kanale — jako kreska ikony nie do odróżnienia, więc skasowałby wyjątek, który miał chronić. Niebieski vs szary rozróżnia natychmiast i **zwalnia czerwień** |
| ~~**O‑4**~~ | ~~Connect~~ | ✅ **ZAMKNIĘTE → ⚪** (§8.2 wykonane). Wstrzymane w §19.17, wykonane teraz, bo **kontekst się zmienił**: po K6 Connect został ostatnim elementem paska na `AccentIconBrush` obok sześciu `AccentBrush` w roli R‑6 |
| ~~**O‑5**~~ | ~~Security Manager~~ | ✅ **ZAMKNIĘTE → `AccentBrush`.** Pozostałość po czasach, gdy przycisk dziedziczył kolor po tym, **o czym** jest; §1.2/3 mówi wprost, że przy kolizji rodzaju ze skutkiem **wygrywa skutek** |
| ~~**O‑3**~~ | ~~Zakres pierwszej implementacji~~ | ⭐ **ROZSTRZYGNIĘTE 2026-08-02 przez R14 (§0.4): podetapami, sortowane od najbardziej oczywistego zysku.** Plan w §11 |
| **O‑4** | ⭐ **Connect w pasku tytułu — zdejmować niebieski czy nie?** §8.2 mówi **R‑7 ⚪** („nie otwiera modułu, działa na zaznaczeniu") i formalnie ma rację. ⛔ **Nie wykonane 2026-08-03, świadomie**, bo §0.5 odpowiada **„nie wiadomo"**: Connect jest **główną akcją tego paska**, a niebieski jest dziś jedyną rzeczą, która odróżnia go od Edytuj / Kopiuj / Rozłącz / Połącz ponownie. Po zdjęciu koloru cała lewa część paska staje się jednolicie szara poza czerwonym koszem — możliwe, że rozpoznanie **spowolni**, a to jest dokładnie mechanizm M3.2b. ⚠ Zauważ, że §11 sam nie ponumerował tego wiersza — prawdopodobnie z tego samego powodu. **Do rozstrzygnięcia w pełnym QA §13.3, na całym pasku naraz** |
| **O‑5** | **Security Manager niesie `IconColor_Role`**, czyli kolor RODZAJU (S1), choć jest przyciskiem otwierającym moduł (R‑6). Jedyny taki przypadek wśród sześciu narzędzi paska. Możliwy świadomy wyjątek („to jest o rolach") albo pozostałość. Poza K2–K7; do §13.3 |

### §9.2 ⭐⭐ P‑1 — PALETA APLIKACJI JEST ZA UBOGA NA ROZRÓŻNIANIE AKTYWNOŚCI (zmierzone 2026-08-04)

> **Nowy, osobny temat systemu kolorów** — otwarty w M3b.3 (`product-polish.md` §19.35) i **świadomie NIE
> rozwiązywany tam**. Decyzja użytkownika: *„Jeśli obecna paleta nie pozwala uzyskać wystarczająco
> rozróżnialnych kolorów, to potraktowałbym to jako osobny temat dotyczący systemu kolorów aplikacji, a nie
> samego raila. Nie chciałbym rezygnować z rozróżniania aktywności tylko dlatego, że obecna paleta okazała
> się zbyt uboga."*

**Ratyfikowany cel:** rail paska statusu rozróżnia **typ aktywności** kolorem, żeby użytkownik rozpoznawał
kątem oka, co robi aplikacja; szczegół niesie tekst sekcji 2/4. Dotyczy pięciu aktywności: połączenie ·
zapytanie SQL · Script Executor · import · debugger (+ trace jako tło pracy), obok severity.

**Blokada, zmierzona:** `Border.Rail` = **2 px**. Severity zajmuje odcień **0°** i **~36°**. A wszystkie
istniejące barwy tożsamości mieszczą się w pasmie **149–215°**:

| kandydat | Dark: kontrast / odcień | Light |
|---|---|---|
| `ConnectedColor` | 7,37:1 / **154°** | 4,56:1 / 149° |
| `DebugLoopIconColor` | 7,02:1 / **174°** | 4,38:1 / 174° |
| `IconColor_Query` | 8,03:1 / **200°** | 6,58:1 / 199° |
| `AccentIconColor` | 5,17:1 / **209°** | 4,81:1 / 215° |

⛔ Odległości: zapytanie↔trace **9°**, połączenie↔debugger **20°**, debugger↔trace **26°**,
zapytanie↔debugger **35°** — w 2‑pikselowej linii to te same kolory.

**Dwie drogi, obie do decyzji użytkownika, żadna nie mieści się w podetapie railu:**
1. **poszerzyć paletę** o odcienie, których produkt nie używa (fiolet ~280°, magenta ~320°);
2. **zmienić nośnik** — 2 px nie unosi rozróżniania odcieni. ⚠ Ale ⛔ §8.5 specyfikacji zabrania wzrostu
   paska statusu, a stała grubość railu jest dziś gwarancją, że zmiana stanu nie przesuwa układu.

**Jadą razem z P‑1, bo korygowanie ich pojedynczo użytkownik odrzucił:**
* **P‑2** — `AccentBrush` na railu daje w Dark **2,89:1**, poniżej progu §10 (3:1 dla elementu UI).
  ⚠ `AccentColor` jest współdzielony (przyciski, fokus), więc to nie jest korekta lokalna.

  ⭐⭐ **UZUPEŁNIONE PRZEZ BRAMĘ §13.3 (2026-08-04): para (`AccentBrush`, `PanelBrush`) ma w trwałej
  chromie DWA konsumenty, nie jednego — drugim jest WSKAŹNIK AKTYWNEJ ZAKŁADKI** (`Border.tab-indicator`,
  2 px, `#2D6BBF` na `#252526`, zmierzone niezależnie: te same 2,89:1 w Dark i 4,81:1 w Light).
  ⚠⚠ **Ale werdykt jest INNY dla obu, i ta różnica jest ważniejsza od samej liczby:**
  * **wskaźnik zakładki — AKCEPTOWALNY.** W skali 1:1 czyta się jednoznacznie w obu motywach, bo ma
    **trzy nadmiarowe współsygnały**: 2 px akcentu + podmiana tła kafelka + etykieta na SemiBold.
  * **rail — NIEAKCEPTOWALNY.** Jest **jedynym** nośnikiem: 2 px na dolnej krawędzi okna, bez
    współsygnału, przechodzące między spoczynkiem i pracą z 1,47:1 na 2,89:1.

  ⭐ Stąd wyostrzenie sformułowania P‑2: problem nie brzmi *„kolor railu jest zły"*, ale *„rail każe jednej
  2‑pikselowej linii nieść sygnał, którego ta para tokenów w Dark nie unosi"*. Przy zmianie palety trzeba
  znać **oba** miejsca — poprawa railu przez podniesienie `AccentColor` ruszy też wskaźnik zakładki,
  który dziś działa.

  ⛔ **NIE dotyczy badge'a `CreateIcon`** (M3.5 / Z‑6), choć też stoi na `AccentBrush` w chromie: tam
  pracuje **solidny dysk 10 j. z białym plusem w środku**, czyli powierzchnia i kontrast wewnętrzny,
  a nie różnica 2 px wobec tła. Zapisane, bo to wygląda na trzeci konsument tej pary i nim nie jest.
* **P‑3** — ⛔⛔ **debugger ma DWIE barwy na jeden fakt w tym samym pasku statusu:** chip maluje
  `AccentIconBrush`, rail maluje `DebugCurrentLineBarBrush`, czyli token **paska bieżącej linii z edytora**.
  Żadna nie jest barwą tożsamości debuggera, a `AccentIconBrush` jest przewidziany do likwidacji (**DC**).

⚠ **Korekta zapisu z `product-polish.md` §19.4.4:** notatka *„trace w Light — jako sygnał słaby"* **nie
dotyczy kontrastu**; `trace` ma najlepszy kontrast z całego zestawu. Jeśli „słaby", to w sensie skojarzenia
barwy.

⭐⭐ **Lekcja metodologiczna, szersza niż kolor: ograniczenie NARZĘDZIA nie jest argumentem za zmniejszeniem
WYMAGANIA.** Rekomendowałem cięcie liczby kategorii pod obecną paletę; użytkownik to odrzucił i miał rację —
poprawna kolejność jest odwrotna: wymaganie zostaje, a niewystarczające narzędzie staje się własnym tematem.

---

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

---

## §11 ⭐⭐ PLAN WDROŻENIA — siedem małych kroków, nie jedna rewolucja

> Realizacja **R14** (§0.4). **Jeden krok = jedna iteracja = jeden commit = jedno obejrzenie na żywo.**
> ⛔ Nie łączyć kroków „bo to i tak ta sama tabela".

### §11.1 ⚠ Dwie klasy kroków — i tylko jedna podlega R14

| Klasa | Definicja | Kryterium odbioru |
|---|---|---|
| **WIZUALNY** | zmienia to, co widać na ekranie | ⛔ **R14: musi być ewidentnym ulepszeniem sam w sobie** |
| **NEUTRALNY** | zero zmiany na ekranie (np. nowy token o identycznej wartości) | bezpieczny z definicji — **nie może pogorszyć UX, bo nic nie zmienia**; wystarczy pomiar dowodzący zerowej różnicy |

### §11.2 Kolejność

| # | Krok | Klasa | Miejsc | Dlaczego tutaj |
|---|---|---|---|---|
| ✅ **K1** | **`ActionRunBrush`** — nowy token o wartości identycznej z `SuccessIconColor`, podstawiony pod Execute procedury / funkcji / Start trace | **neutralny** | 3 | ⭐ **WYKONANY 2026-08-03** (`product-polish.md` §19.15). Zero zmiany wizualnej — pomiar potwierdził identyczność wartości w obu motywach; 3 konsumentów zgodnie z planem |
| ✅ **K2** | **Destrukcja: 🟡 → 🔴** — Usuń połączenie · Usuń zapytanie · Wyczyść wszystkie zapytania | wizualny | 3 | ⭐⭐ **WYKONANY 2026-08-03** (`product-polish.md` §19.16), czeka na QA wizualne. Zmierzone: przycisk nie zgadzał się z **własnym menu kontekstowym** w tym samym panelu; kod już klasyfikował operację jako destrukcję (`IsDestructive = true`). Realizacja W5 |
| ✅ **K3** | **Edytuj → ⚪** | wizualny | ⚠ **1**, nie 3 | ⭐ **WYKONANY 2026-08-03.** ⚠⚠ **Pomiar poprawił §8.2: „Edytuj (Procedure, Function)" to NIE są przyciski**, tylko wiersze `UpdateChange` w karcie podsumowania zmian — **stan, nie akcja**, więc §2 wyklucza je z języka. „Edit Connection" był już neutralny. Realne miejsce: **zmiana nazwy profilu w Data Import** |
| ✅ **K4** | **Wskaż plik: `AccentIconBrush` → `AccentBrush`** (Data Import) | wizualny | 1 | ⭐ **WYKONANY 2026-08-03.** ⚠ Zmierzone przed zmianą, bo wyglądało na ryzyko kontrastu w Dark: to **ten sam odcień, który pięć narzędzi R‑6 nosi w pasku tytułu** od dawna i który został odebrany. W Light oba tokeny mają identyczną wartość |
| ⛔ **K5** | ~~**Dodaj → ⚪** · **Szukaj w widoku → ⚪**~~ | — | **0** | ⛔ **KROK ODPADA PO POMIARZE 2026-08-03.** „Dodaj (Procedure, Function)" to te same wiersze **stanu** co w K3 (`InsertChange`), a „Szukaj w widoku (Trace)" to **glif w polu tekstowym**, nie przycisk — pociemnienie go uczyniłoby dekorację głośniejszą od treści, więc §0.5 odpowiada „nie" |
| ✅ **K6** | **Odśwież → ⚪** — metadane, dane tabeli, Data Import | wizualny | 3 | ⭐ **WYKONANY 2026-08-03.** ⭐⭐ Wbrew obawie z planu **wzmacnia** pasek, a nie wygasza: w tym samym pasku niebieski niesie R‑6 „wejście do modułu" (5 przycisków), a odświeżenie modułu nie otwiera — jeden kolor znaczył dwie rzeczy. ⛔ Sześć wejść do modułów **zostaje** kolorowych |
| ✅ **K7** | **Commit / Rollback → `CommitButtonBrush` / `RollbackButtonBrush`** (decyzja DD) | ⭐ **neutralny** | 6 | ⭐ **WYKONANY 2026-08-03.** Zaczęty od wartości (§7.2): tokeny dostały **dostrojone pary** `Success`/`Danger` zamiast surowego Material identycznego w obu motywach. ⇒ wygląd bez zmiany, role z własnymi tokenami — ten sam zabieg co K1 |
| ✅ **§7.4** | **Pause (Trace) → `WarningIconBrush`** | wizualny | 1 | wykonane przy okazji, jak przewidywał §11.3: `WarningBrush` zostaje dla **tekstu i komunikatów**, R‑5 ma jeden token ikonowy |

### §11.3 ⛔ Czego plan świadomie NIE obejmuje

| | Powód |
|---|---|
| **6 narzędzi w pasku tytułu** | ⭐ **ZOSTAJĄ KOLOROWE** — rola R‑6. To nie jest odłożenie, to jest rozstrzygnięcie |
| ~~Comment / Uncomment~~ | ✅ zamknięte — **O‑2** |
| ~~Debugger Continue~~ | ✅ zamknięte — **O‑1** |
| **Likwidacja `AccentIconBrush` / `InfoIconBrush`** | decyzja **DC** → M4.3/M5, poza językiem. ⚠ Oba tokeny **nadal mają konsumentów** i nie są sierotami: `AccentIconBrush` maluje chip stanu debuggera (W‑5), żarówkę Quick Fix i złożony znak `DebuggerIcon`; `InfoIconBrush` niesie Comment (W‑1) |
| ~~`WarningBrush` vs `WarningIconBrush`~~ | ✅ wykonane przy K3–K7 — R‑5 ma jeden token ikonowy, `WarningBrush` został przy tekście i komunikatach |
| **Menu kontekstowe** | osobny, już spójny system (§2) |

### §11.4 ⭐⭐ Przegląd domykający (2026-08-03) — język jest wdrożony w całości

Po K1–K7 użytkownik zgłosił, że **byliśmy zbyt zachowawczy** i że część zmian nie została doprowadzona
do końca (przykład: Security Manager). Przegląd całego produktu z dokumentem w ręku domknął **pięć**
pozostałości: Security Manager · Connect · Debugger Continue · Uncomment ×4 · Waliduj (Data Import —
niósł tę samą ikonę **i ten sam zielony** co Zatwierdź, w jednym pasku).

⭐ **Stan końcowy, zmierzony: 230 `SvgIcon` w widokach, 81 z kolorem — i ANI JEDEN przycisk akcji nie
stoi poza językiem.** Cały pozostały kolor to role R‑1…R‑7, dwa nazwane wyjątki (**W‑1**, **W‑3**),
`IconColor_*` (S1), `OnAccentBrush` (S4) oraz stany i dekoracje wykluczone przez §2.

⚠ **Reguła, którą ten przegląd potwierdził trzeci raz:** *zgodność z dokumentem nie jest tym samym co
spójność produktu.* Wszystkie pięć pozostałości było „zgodnych" w tym sensie, że nikt nie zapisał ich
jako defektu — a widać je było na pierwszy rzut oka na gotowym ekranie.
