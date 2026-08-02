# EmberTern — PROMPT STARTOWY: wdrożenie języka kolorów (krok K1)

> Wklej to jako pierwszą wiadomość nowej sesji. Dokument jest **samowystarczalny w zakresie stanu,
> decyzji i planu** — do implementacji sięgniesz jeszcze po dwa dokumenty wskazane w §1.

---

## 0. ⛔⛔ PRZECZYTAJ TO NAJPIERW — jednozdaniowe streszczenie wczorajszej sesji

**Cztery zmiany z rzędu zostały odrzucone po obejrzeniu w działającej aplikacji, mimo że każda działała
i każda usuwała zmierzony defekt** — bo za każdym razem reguła była doprowadzana do logicznej
konsekwencji zamiast konfrontowana z produktem. Dlatego dziś **nie projektujesz nic nowego**: język
kolorów jest **zaprojektowany i zaakceptowany**, a Twoim zadaniem jest wykonać jego **pierwszy, celowo
najbezpieczniejszy krok**.

---

## 1. Co przeczytać, zanim napiszesz linijkę kodu

| # | Dokument | Zakres |
|---|---|---|
| 1 | **ten plik** | w całości |
| 2 | ⭐⭐ **`docs/design/color-language.md`** | **w całości** — to jest dokument, który wykonujesz |
| 3 | `docs/design/product-polish-m3-handover.md` | §5 (reguły R1–R14) · §6 (procedura iteracji) · §8 (kolejność) · §9 (17 pułapek) |
| 4 | `product-polish.md` **§20** | inwentarz akcji i kolorów — dane, na których stoi język |

⛔ **Nie czytaj na starcie:** `product-polish.md` §15, §18.1–§18.11, handoverów M2a/M2b/M2c.
⚠ **§19.10–§19.14** czytaj **tylko wtedy**, gdy chcesz zrozumieć, dlaczego coś zostało odrzucone —
streszczenie masz w §4 poniżej.

---

## 2. Stan projektu

| | |
|---|---|
| **Branch** | `feat/product-polish` |
| **Build** | 0 błędów / 0 ostrzeżeń |
| **Suite** | **7133** zielony w trzech partycjach (**7031 + 48 + 54**) |
| **Smoke** | czysty · **Drzewo** czyste |
| **Etap** | M0–M2c ✅ · **M3.1 ZAMKNIĘTE** ✅ · **M3.2a** — z czterech ruchów został jeden · **M3.2b** ⛔ wycofane w całości · **projekt języka kolorów** ✅ zaakceptowany |

### 2.1 ⚠ Cztery commity czekają na push — zapytaj, nie pushuj sam

```
33bd8df  docs(color-language): projekt jezyka kolorow — DO AKCEPTACJI
d7d26de  docs(product-polish): §20 — inwentarz akcji i kolorow calego produktu
07ad789  revert(product-polish): wycofanie M3.2b w calosci + diagnoza wzorca
70c2eb2  feat(product-polish): M3.2b — semantyka kolorow §7.5   ← wycofany przez 07ad789
7f36b0f  fix(product-polish): odbior M3.2a cz.2 — wycofanie B1, ratyfikacja R13
f168ca0  fix(product-polish): odbior M3.2a — wycofanie B2 i B3
963ab44  feat(product-polish): M3.2a — H-3
```

Oba remote'y stoją na **`b16f476`**. ⛔ **Push wyłącznie po akceptacji użytkownika** — to stała reguła
projektu.

---

## 3. Co zostało wykonane i ZOSTAJE w produkcie

| Etap | Co zostało |
|---|---|
| **M3.1a–M3.1f** | całość: rytm pionowy chromy · cztery sekcje Status Bara · rail · chip transakcji z czasem · chipy Trace i Debuggera · sekcja postępu + operacja referencyjna |
| **M3.2a** | ⭐ **tylko T2**: Export DDL przeniesiony na **koniec** kolumny 0 paska tytułu (za kreatory) ⇒ **pasek tytułu nie ma ani jednej bramki `IsVisible`, która cokolwiek przesuwa**. H‑3 dla paska tytułu jest **zamknięte** |
| **M3.2a — porządek** | usunięte lokalne `Padding="10,4"` z przycisku Cancel (biło rolę `Pad.Button`) |
| **projekt** | `color-language.md` — zaakceptowany, **nic nie wdrożone** |
| **pomiar** | `product-polish.md` §20 — inwentarz akcji i kolorów całego produktu |

---

## 4. ⛔⛔ Co zostało ŚWIADOMIE ODRZUCONE — nie próbuj tego ponownie

> Każda z tych zmian **działała technicznie** i **usuwała zmierzony defekt**. Odrzucono je po
> obejrzeniu na żywo. To nie są niedokończone zadania.

| # | Odrzucone | Dlaczego | Zapis |
|---|---|---|---|
| **1** | **Commit/Rollback dokowane do prawej krawędzi paska** | ⭐ **GRUPA SEMANTYCZNA BIJE STABILNOŚĆ POZYCJI.** Argument zasięgowy był poprawny (to jedyna para mówiąca o *transakcji*), ale użytkownik szuka poleceń wg **sąsiedztwa z akcją, którą właśnie wykonał**. Autor zmiany sam ich nie znalazł | §19.11 |
| **2** | **Wspólna podłoga szerokości Execute/Cancel** (`MinWidth=156`) | usuwała drganie 38 px przy F5, ale **rozdymała akcję główną ponad jej treść**. ⭐ R5 od drugiej strony: *nieważne, skąd rozmiar pochodzi — liczy się, co komunikuje* | §19.11 |
| **3** | **Rezerwacja slotu sekcji 1 toolbara** (43 px) | zostawiała **pustą dziurę w SQL Editorze**. ⇒ **R13** | §19.12 |
| **4** | **Całe M3.2b — wyszarzenie kolorów** | ⛔ **Cztery straty naraz:** Execute procedury stracił zielony (naturalny kolor „Uruchom") · Comment/Uncomment straciły **celowe** rozróżnienie, **zamówione wcześniej przez użytkownika** · sześć narzędzi paska tytułu straciło kolor, choć **otwierają moduły** (rola R‑6) · **żółte Saved Queries — jedyne miejsce z naprawdę wątpliwą semantyką — zostały nietknięte**, bo leżały poza mierzonym obszarem | §19.13 + §19.14 |
| **5** | **Wariant pełnych kotwic sekcji toolbara** | zmierzone: ~617 px stałej rezerwy ⇒ **~500 px dziur** na uboższych zakładkach. To R13 pomnożone przez pięć | §19.10.3 |

### 4.1 ⛔⛔ Trzy rzeczy, których nie wolno „naprawić" — bo są celowe

1. **Comment / Uncomment mają różne kolory.** Zamówione przez użytkownika: ikony są bardzo podobne
   i kolor pozwala je rozpoznać błyskawicznie. **M3.2b uznało to za defekt i to był błąd.**
2. **Toolbar dokumentu przesuwa się przy zmianie rodzaju zakładki (68 px) i przy F5 (38 px).**
   Świadomie zaakceptowany kompromis. Oba komentarze w `MainWindow.axaml` mówią to wprost.
3. **Sekcja 1 toolbara nie rezerwuje miejsca, gdy jest pusta.** R13.

---

## 5. ⭐ Decyzje RATYFIKOWANE — nie otwierać ponownie

### 5.1 Reguły stałe (pełna lista: handover §5)

| # | Reguła |
|---|---|
| **R8** | kryterium odbioru: *„czy wygląda to jak dopracowana aplikacja komercyjna?"*; **pomiar jest narzędziem, nie argumentem końcowym** |
| **R12** | celem jest usunięcie **nieuzasadnionych** wartości lokalnych, nie wyzerowanie licznika |
| **R13** | ⭐ **nie rezerwujemy miejsca na element, który w danym kontekście nigdy się nie pojawi** |
| **R14** | ⭐⭐ **każdy krok musi być ewidentnym ulepszeniem UX sam w sobie.** *„Wolę pięć małych, oczywistych poprawek niż jedną dużą rewolucję."* ⚠ Nie dotyczy kroków **neutralnych wizualnie** |

### 5.2 Język kolorów (pełny zapis: `color-language.md`)

| # | Decyzja |
|---|---|
| **W1** | **ta sama akcja → ten sam kolor w całej aplikacji** |
| **W2** | różne akcje, które łatwo pomylić, **mogą** świadomie mieć różne kolory |
| **W3** | ⛔ **nie budujemy systemu „im mniej kolorów, tym lepiej"** |
| **W4** | Execute i Commit to **dwie role** → osobne tokeny, **na razie ten sam odcień** |
| **W5** | 🟡 = ostrzeżenie · uwaga · wstrzymanie. **NIGDY destrukcja** |
| **W6** | tożsamość modułu tylko **wewnątrz** modułu; wyjątek: w pasku globalnym wolno, gdy element niesie **STAN**, nie akcję |

### 5.3 Decyzje etapu

**DA** katalog wygrywa (28 → 24) · **DB** wiersz drzewa **zostaje 24** · **DC** likwidacja
`AccentIconBrush`/`InfoIconBrush` → **M4.3/M5**, poza językiem · **DD** Commit/Rollback przechodzą na
`CommitButtonBrush`/`RollbackButtonBrush` — ⚠ **ale dopiero po dostrojeniu ich wartości per motyw**
(`color-language.md` §7.2).

---

## 6. ⏸ Pytania OTWARTE — nie rozstrzygaj sam

| # | Pytanie | Dlaczego czeka |
|---|---|---|
| **O‑1** | **Debugger Continue** — dziś `AccentIconBrush`, ratyfikowany w **D15.2 Seam A** jako „jedyna akcja pierwszorzędna debuggera"; wg roli R‑1 powinien być zielony | **kolizja dwóch ratyfikacji** na powierzchni już odebranej wizualnie |
| **O‑2** | **Comment / Uncomment — jakie dwa kolory?** Rozróżnienie zostaje (W‑1), ale `Danger` na Uncomment osłabia jednoznaczność czerwieni. Rekomendacja: wariant **(c)** — oba w obrębie niebieskiego | `color-language.md` §9.1 |
| **⏸** | **`WarningBrush` vs `WarningIconBrush`** — dwie nazwy na jedną rolę | §7.4; porządek tokenów, przy okazji K3 albo osobno |
| **⏸** | **QA wizualne M3.2a** (T2 — Export DDL na końcu paska tytułu) | użytkownik nie potwierdził wprost po ostatnim wycofaniu |

---

## 7. ⭐⭐ OD CZEGO ZACZYNASZ: krok K1

> Pełny opis: `color-language.md` **§11.2**.

**K1 — nowy token `ActionRunBrush`, klasa: NEUTRALNY WIZUALNIE.**

| | |
|---|---|
| **Co** | dodać token `ActionRunBrush` w **obu** motywach, o wartości **identycznej z `SuccessIconColor`** (Dark `#6DBE7E` · Light `#2E8B4F`), i podstawić go pod trzy miejsca dziś używające `SuccessIconBrush` dla roli **R‑1 Uruchom**: Execute procedury · Execute funkcji (`MainWindow.axaml`) · Start trace (`TraceMonitorTabView.axaml`) |
| **Po co** | realizuje **W4** — R‑1 dostaje własny token, więc przyszłe rozdzielenie odcieni Execute i Commit nie będzie wymagało ruszania Commita |
| **Dlaczego pierwszy** | ⭐ **zero zmiany na ekranie** (ta sama wartość) ⇒ nie może pogorszyć UX ⇒ jedyny krok, który nie podlega R14 |
| **Dowód wykonania** | pomiar pokazujący, że wartość `ActionRunBrush` jest **identyczna** z `SuccessIconColor` w obu motywach, oraz że `SuccessIconBrush` nadal ma swoich konsumentów (Commit i pozostałe) |
| **Czego NIE robić w K1** | ⛔ nie dotykać Commita · ⛔ nie zmieniać żadnej wartości koloru · ⛔ nie ruszać debuggera (O‑1) · ⛔ nie łączyć z K2 |

### 7.1 Kolejne kroki (nie w tej iteracji)

**K2** destrukcja 🟡 → 🔴 (Usuń połączenie · Usuń zapytanie · Wyczyść wszystkie) — ⭐ **najbardziej
oczywisty zysk w planie, użytkownik zgłosił to sam** · **K3** Edytuj → ⚪ · **K4** Wskaż plik
`AccentIconBrush` → `AccentBrush` · **K5** Dodaj / Szukaj-w-widoku → ⚪ · **K6** Odśwież → ⚪
(⚠⚠ **największe ryzyko**, dotyka paska tytułu — obejrzeć osobno) · **K7** Commit/Rollback (DD, po
dostrojeniu tokenów).

---

## 8. Obowiązkowa kolejność

```
analiza → propozycja (AKCEPTACJA) → implementacja → uruchomienie aplikacji + QA w obu motywach
  → dotnet build (0/0)
    → dotnet test (TRZY partycje, OSOBNO)
      → smoke
        → dokumentacja (product-polish.md §19 + color-language.md, jeśli coś się zmienia)
          → commit (kod + opis iteracji razem)
            → push na oba remote'y — WYŁĄCZNIE po akceptacji użytkownika
```

⚠⚠ **Nigdy nie łącz `dotnet build` i `dotnet test` w jednym poleceniu — deadlock.**
⚠ **Zabij `EmberTern.exe` przed przebudową** — inaczej MSB3021/MSB3027.

**Trzy partycje** (⚠ `ConnectionExpandBindingProbe` biegnie **sam**):

```
--filter "FullyQualifiedName!~ConnectionExpandBindingProbe&FullyQualifiedName!~SettingsCenterViewTests&FullyQualifiedName!~BrandingPresentationTests&FullyQualifiedName!~DesignTokenApplicationTests&FullyQualifiedName!~TabStripPresentationTests"
```

oraz odwrotność z `|`, oraz `ConnectionExpandBindingProbe` osobno. Stan: **7031 + 48 + 54 = 7133**.
⚠⚠ **Filtr jest listą nazw i starzeje się cicho** — kryterium: *czy klasa konstruuje kontrolki
Avalonii*. Jeśli tak, dopisz ją.

---

## 9. ⛔ Czego absolutnie nie wolno próbować

1. ⛔ **Nie projektuj języka kolorów ponownie.** Jest zaakceptowany. Wykonuj `color-language.md` §11.
2. ⛔⛔ **Nie doprowadzaj reguły do logicznej konsekwencji.** Pułapka 17: *reguła opisuje to, co już
   jest dobre; nie jest mandatem do zmiany wszystkiego, co do niej nie pasuje.*
3. ⛔ **Nie łącz kroków K1–K7.** R14: jeden krok = jedna iteracja = jedno obejrzenie na żywo.
4. ⛔ **Nie odbarwiaj sześciu narzędzi w pasku tytułu** — rola R‑6, **rozstrzygnięcie**, nie odłożenie.
5. ⛔ **Nie ujednolicaj Comment/Uncomment** — czeka na O‑2, do tego czasu bez zmian.
6. ⛔ **Nie ruszaj Continue w debuggerze** — O‑1.
7. ⛔ **Nie przenoś Commit/Rollback** ani na prawo, ani do paska statusu (§8.4.5).
8. ⛔ **Nie rezerwuj miejsca na element, którego w danym kontekście nie będzie** — R13.
9. ⛔ **Nie likwiduj `AccentIconBrush`/`InfoIconBrush`** — DC, M4.3/M5.
10. ⛔ **Nie ujednolicaj menu kontekstowych z przyciskami** — osobny, już spójny system.
11. ⛔ **Nie rozszerzaj katalogu, żeby domknąć kolizję** — K1–K11 czekają na §13.3.
12. ⛔ **Nie naprawiaj przy okazji rzeczy spoza zakresu** — mierz, opisz, zapisz, nie rozwiązuj bez decyzji.

---

## 10. ⭐⭐ Trzy pytania kontrolne przed KAŻDĄ zmianą wyglądu

Wynikają wprost z czterech odrzuceń:

1. **Czy ten element jest niezgodny, bo to błąd — czy dlatego, że ktoś świadomie tak chciał?**
   Sprawdź `color-language.md` §5. ⚠ Brak wpisu **nie dowodzi**, że wyjątku nie było — dowodzi, że nie
   został zapisany. Wtedy **zapytaj, nie zmieniaj**.
2. **Co użytkownik traci, jeśli się mylę?** Jeśli odpowiedź brzmi *„rozpoznawalność"* — nie zmieniaj
   bez obejrzenia na żywo.
3. **Czy mierzę tam, gdzie problem jest, czy tam, gdzie patrzę?** 91 % ikon aplikacji jest już
   neutralne; M3.2b wyciszało, bo wszystkie kolorowe skupiają się w dwóch paskach.

> **Reguła prowadząca (użytkownik):** *„Dokument ma prowadzić produkt. Nie produkt dokument."* ·
> *„Pomiar nadal jest obowiązkowy. Ale nie jest już celem. Jest tylko narzędziem."*
