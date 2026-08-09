# Product Polish M4 — prompt startowy na MIGRACJĘ EKRANÓW (M4.1–M4.4)

> **Do wklejenia na początku następnej sesji.** Zastępuje `product-polish-m4-next-session.md`, który opisywał
> wejście do M4 i pracę już wykonaną — czytaj go tylko dla „dlaczego", nigdy dla „co dalej".
>
> ✅ **DECYZJE PROJEKTOWE M4 SĄ KOMPLETNE (2026-08-08).** Rejestr kolizji **K1–K15 zamknięty w całości**,
> oba bloki decyzyjne odebrane po QA wizualnym w obu motywach. Repo czyste, oba remote'y zsynchronizowane.
> ⛔ **Nie otwieraj sesji od raportowania stanu ani od pytań, czy coś zostało domknięte** — jeżeli
> `git status` jest czysty, to jest cała odpowiedź.
>
> ⏭ **Następny krok: M4.4 — 16 dialogów + okna + `GrowingDialogBehavior` (M‑5). To OSTATNI etap migracji M4.**
>
> ✅ **ZROBIONE I ODEBRANE:** **M4.1** (SQL Editor · Script Executor · Data Import, §19.39) · **M4.2**
> (edytory obiektów, §19.40) · **M4.2b** (drzewa „Zależności", §19.41) · **M4.3b** (Debugger · Trace ·
> Session · Security · Performance, §19.42) · **M4.3c** (`Button.seg`, §19.43).
>
> ⚠⚠ **PRZECZYTAJ TO PRZED M4.4 — DWIE LEKCJE Z M4.3, KTÓRE ZMIENIAJĄ SPOSÓB PLANOWANIA ETAPU.**
>
> **(1) ⭐⭐ M4.3 nie był sweepem literałów, tylko ODBIOREM DECYZJI, KTÓRYCH NIKT NIE PODJĄŁ — i M4.4
> najprawdopodobniej też nim będzie.** W pięciu plikach M4.3 stało **19 komentarzy „rozstrzyga §13.3"**,
> pokrywających praktycznie każdą pozostałą tam wartość lokalną; brama §13.3a nie podjęła ani jednej, żadna
> nie dostała numeru K, a rejestr został ogłoszony „zamkniętym w całości" (#340).
> ⭐ **Pierwszy ruch w M4.4: `grep -rn "13\.3" src/EmberTern.App/Views/*Dialog*.axaml` i pokrewne** — sieroty
> skupiają się dokładnie tam, gdzie liczniki pokazują resztę, bo wartość sparkowana to wartość niezmigrowana.
> ⚠ Zmierzone w M4.3: `ChoiceDialog`, `ConfirmDialog` i `ForeignKeyDialog` **już mają takie odesłania**
> (grupa „TextBlock 13 px"), więc w M4.4 to nie jest hipoteza.
>
> **(2) ⚠⚠ POMIAR OBALIŁ TRZY MOJE WŁASNE PRZESŁANKI W JEDNYM ETAPIE — dwie w strażnikach, jedną w zakresie.**
> Strażnik grupował ikony po NAZWIE GEOMETRII i padł na przycisku, który ma inną rolę (#341); drugi łapał ogon
> `MinHeight` zamiast wysokości ramki; a cała iteracja M4.3c była wydzielona na przesłance o priorytecie stylu,
> którą podsadzenie **obaliło** (#342 — Avalonia rozstrzyga specyficznością selektora, nie kolejnością).
> ⭐ **Wniosek operacyjny: podsadzenie, które NIE zapala testu, jest wynikiem, a nie usterką procedury** —
> to wtedy dowiadujesz się, że mierzysz co innego, niż deklarujesz.
>
> **(3) ⭐ Test BEHAWIORALNY nadal obowiązuje, ale ze zmierzoną granicą.** M4.3b świadomie go nie dodał
> (zmieniał wyłącznie metryki i role na `Border`/`TextBlock`/`SvgIcon` — zero zdarzeń, klawiatury, zaznaczenia
> i szablonowania); M4.3c dodał, bo ruszał STYL KONTROLKI. ⚠ Kryterium brzmi więc: *czy iteracja może
> sprawić, że reguła przestanie DOCIERAĆ do kontrolki* — a nie „czy plik jest duży".
> ⚠ Nowa klasa headless **dołącza do istniejącej** (lista nazw w filtrze partycji jest krucha i ręczna).

---

## 1. Punkt odniesienia

**`feat/product-polish`**, build 0/0, suite **8334** w trzech partycjach (**8182 + 97 + 55**), smoke czysty.
⚠ Liczbę testów **zmierz, nie przepisuj** — ta linia dryfowała w projekcie wielokrotnie; stoi tu tylko po to,
żeby rozpoznać stan „nic się nie zmieniło od zamknięcia".

⚠ **Znany flake, nie ogłaszany naprawionym:** `SettingsLoadHealthTests.ConcurrentSaves_NeverLeaveSettingsUnreadable`
(test na `Parallel.For`) potrafi zaświecić raz na czerwono i przechodzi solo. Zobaczysz go — nie jest to
regresja Twojej pracy. **Powtórz partycję, zanim zaczniesz szukać przyczyny.**

⛔ **Kryterium zieloności to SUMA, nie „0 niepowodzeń"** — run, w którym cała partycja nie wystartowała,
też raportuje zero błędów (zmierzone 2026-08-05).

---

## 2. Co przeczytać i w jakiej kolejności

1. **`CLAUDE.md`** — „Current state" (dwa pierwsze wpisy to M4) + „UI styling rules" (reguły 1–11, zwłaszcza
   **#8** trzy trasy restylowania, **#9** zakaz literałów tam, gdzie jest rola, **#10** kontener rozstrzyga
   wielkość, **#11** reguła formułowana pozytywnie).
2. **`docs/design/product-polish.md`** — ⛔ nie w całości:
   * **§13** plan etapu (gdzie leży M4.1–M4.4 i od czego zależy) + **§13.0** DoD + **§13.0.1** zależności;
   * **§19.37** as-built bloku gęstości · **§19.38** as-built bloku typografii;
   * **§18.R** rejestr kolizji — **cały zamknięty**, czytaj jako zapis, nie jako listę zadań;
   * **§0.1 / §0.1.1 / §0.1.2** — zasady nadrzędne wobec katalogu.
3. **`docs/design/product-polish-m3-handover.md`** — **reguły R1–R18** (§5) i **21 pułapek** (§9).
4. **`docs/design/color-language.md`** — **§0.5** przed każdą zmianą koloru i **§6** przy nowej akcji.
5. **`docs/gotchas.md`** — **#332 · #333 · #334** (świeże, z M4), **#322**, **#284**, **#285**.

---

## 3. ⭐ Reguły, które w M4 decydowały najczęściej

| # | reguła |
|---|---|
| **R7** | nie łatać pojedynczego ekranu, gdy defekt jest app-wide |
| **R8** | kryterium odbioru: *„czy to wygląda jak dopracowana aplikacja komercyjna?"* |
| **R12** | **błędna rola jest gorsza od wartości lokalnej**; celem jest usunięcie wartości NIEUZASADNIONYCH, nie wyzerowanie licznika |
| **R16** | pomiar jest narzędziem DIAGNOSTYCZNYM, kryterium odbioru jest ekran; **test zielony na złym ekranie jest gorszy niż brak testu** |
| **R17** | zgodność z dokumentem ≠ spójność produktu; przegląd całej powierzchni jest osobnym krokiem |
| **R18** | ⭐ **przy równej czytelności wygrywa wariant GĘSTSZY** (ratyfikowana 2026-08-08). ⚠ Rozstrzyga REMIS — warunek „bez pogarszania czytelności" jest pierwszy |
| **pułapka 17** | reguła OPISUJE to, co już jest dobre; element niezgodny z regułą bywa wyjątkiem, który DZIAŁA |

---

## 4. 🔒 Decyzje M4 — ratyfikowane, do NIEOTWIERANIA

### 4.1 Blok gęstości (§19.37)

| decyzja | treść |
|---|---|
| **A‑3** | chroma ma **dwa nazwane poziomy**: `Size.Icon.Toolbar` (16) — ikona jako samodzielna AKCJA (pasek narzędzi, przycisk okna, domyślna `ControlTheme`); `Size.Icon` (14) — ikona **w wierszu** (zakładka, drzewo, menu, etykieta na powierzchni roboczej). ⭐ To drabina `Size.Control` 24 / `Size.ControlToolbar` 22 przeniesiona o poziom niżej |
| **B‑1** | wiersz drzewa: `Size.Icon` + `Space.Xs`; 41 par w 13 plikach. Wysokość wiersza bez zmian |
| **C‑1** | `Size.Row.GridEdit` = **30** dla każdej siatki z edytorem w komórce (minimum arytmetyczne to 28: padding 4 + `Size.Control` 24) |
| **D** | podłogi list w pasku importu: `Transaction` i `Errors` zdjęte, `Profile` = 140 |

### 4.2 Blok typografii (§19.38)

| decyzja | treść |
|---|---|
| **A‑2** | `Text.SectionHeader` = **12 SemiBold** (interlinia 17). Rola nie może być mniejsza od `Text.Application`, nad którym stoi — pilnuje tego strażnik |
| **B‑1** | `Text.Toolbar` **wycofana** (duplikowała `Text.Compact` ROLĄ); tekst paska narzędzi to `Text.Compact` |
| **C** | K9: bazowy `TabItem` → `Text.Compact`. 🔒 **K4 (`PlanLead`) ZOSTAJE 13** — wartość lokalna z ratyfikowanym powodem, ⛔ nie „dokańczać" |
| **D** | `MinHeight="26"` Expandera usunięte · chip transakcji na `Space.Xs` · nowa rola `Radius.Tab` = 4 |
| **K5** | ⛔ skreślone — nie miało przedmiotu |

### 4.3 ⛔ Czego NIE wolno ruszać bez osobnej decyzji

* **„tęcza ikon"** w pasku narzędzi (§13.3a.3 — wycofana z uzasadnieniem, kolory ikon to S1);
* **pasek zakładek** (M3.3), **menu kontekstowe** (Keyboard Manager), **Metadata Explorer** — odebrane;
* **`field-label`** i jego 164 użycia — wariant „obniż `Text.Application` do 11" (A‑3 bloku typografii)
  **był rozważany i NIE został wybrany**;
* **9 px i 12 px w edytorach stojących w wierszu siatki** — decyzje KONTENERA, ratyfikowane w §18.0.5/3;
* **`Cascadia Code` vs `Cascadia Mono`** — backlog sprintu UX, nie M4.

---

## 5. ⏭ M4.1–M4.4 — punkt startowy

Zakres z §13:

| etap | zakres |
|---|---|
| ~~M4.1~~ ✅ | SQL Editor · Script Executor · Data Import — as-built §19.39 |
| ~~M4.2~~ ✅ | edytory obiektów (10) — as-built §19.40 |
| ~~M4.2b~~ ✅ | drzewa „Zależności" — as-built §19.41. ⚠ **17, nie 18** (18. to `MemberGroups`, inne drzewo), i **nie na `TreeListView`, tylko na `SidebarFlatController`** — §13.2 odrzucało tę drogę przesłanką, którą pomiar obalił |
| ~~M4.3b~~ ✅ | Debugger · Trace · Session · Security · Performance — as-built §19.42. ⚠ **Nie sweep, tylko odbiór 19 sparkowanych decyzji** (#340) |
| ~~M4.3c~~ ✅ | `Button.seg` — as-built §19.43. ⚠ Przesłanka o priorytecie stylu **obalona pomiarem** (#342) |
| **M4.4** ⏭ | **16 dialogów + okna + `GrowingDialogBehavior` (M‑5)** ← TU ZACZYNASZ. **Ostatni etap migracji M4** |

### 5.1 ⭐ Co migracja ekranów realnie oznacza po zamknięciu rejestru

Rama jest przyjęta, więc **M4.x nie projektuje — konsumuje**. Praktycznie na każdym ekranie:

1. wartości lokalne, które mają rolę → na rolę; wartości bez roli → **zostają z zapisanym powodem** (R12);
2. **sweep literałów**, dla którego istnieją już DWA sufity z liczbami — ⚠ **stan po M4.3 (zmierzony
   2026-08-09), nie z początku M4**:
   * **14 literałów rozmiaru ikony w 6 plikach** (`IconSizeLiteralBaseline`) — z czego **5 to B1**
     (`TableDetailTabView`, prywatne PK/FK/Unique, czeka na decyzję wizualną) i **3 to Trace** (ogon 13 px),
     więc **w zasięgu M4.4 nie ma ani jednego**;
   * **31 literałów `FontSize` w 12 plikach** w oknie licznika (`FontSizeBaseline`) — **w dialogach
     zostają 3** (`ChoiceDialog`, `ConfirmDialog`, `ForeignKeyDialog`, wszystkie z grupy „TextBlock 13 px");
3. ⚠ sufit **schodzi razem z pracą** — strażnik `…HasNoStaleEntries` wymaga obniżenia wpisu, a nie
   wyzerowania go, więc „ile zostało" pozostaje liczbą, a nie opinią.
4. ⚠⚠ **Spadek licznika bywa PRZENIESIENIEM, nie migracją** (zmierzone w M4.3c): `Measure` skanuje wyłącznie
   `Views/` + `Controls/`, więc wartość przeniesiona do `Themes/ControlStyles.axaml` **znika z licznika,
   choć istnieje dalej**. ⭐ Zapisuj to przy wpisie bazowym, inaczej spadek udaje postęp.
5. ⚠ **Komentarz cytujący składnię atrybutu LICZY SIĘ jak deklaracja** — `Measure` czyta plik regeksem i nie
   pomija komentarzy. W M4.3c moje własne zdanie wyjaśniające podniosło licznik pliku o 1. Naprawa:
   przeredagować komentarz (nie pisz `Nazwa="wartość"` w prozie), **nie** podnosić sufit.

### 5.2 ⚠⚠ Trzy rzeczy, które zaskoczą, jeśli się o nich nie wie

* ⭐ **Domyślny rozmiar ikony (16) pochodzi z `ControlTheme`, nie z widoku** — 191 z 355 deklaracji nie podaje
  rozmiaru. Ikona paska narzędzi **nie potrzebuje** żadnego atrybutu i to jest poprawna droga, a nie brak
  decyzji (#332).
* ⭐ **Okno licznika `FontSize` to `Views/` + `Controls/`** — poza nim leży **29 deklaracji**
  (`Completion/*.cs` 16 — karta hover, Quick Info, Parameter Helper; `Themes/PickerTemplates.axaml` 12;
  `Sql/` 1). ⏸ **Poszerzenie okna zostało zbudowane i WYCOFANE**, bo ten sam `Measure` obsługuje `FontFamily`,
  czyli temat czcionki monospace z backlogu. **Wymaga osobnej decyzji użytkownika** — pomiar stoi
  w komentarzu klasy `DesignTokenComplianceTests`.
* ⚠ **Strażnik pilnujący przesłanki potrafi paść na migracji, nie na regresji** (#333). W bloku gęstości dwa
  testy zgłosiły „wiersz nie deklaruje już stałej wysokości", bo czytały literał, a wartość przeszła na rolę.
  **Lekarstwem jest rozwiązywanie roli, nie osłabianie asercji** — wzorzec jest w `EditableGridSeamTests`
  i `GridDateEditorTests`.

### 5.3 ⛔ Kolejność i granice

* ⚠ **Ten punkt opisywał kolejność M4.2b i jest już HISTORIĄ** — zostaje jako zapis, nie jako instrukcja:
  wymagał, żeby drzewa zależności **nie** migrowały na `SidebarFlatController` (§13.2), a pomiar tę przesłankę
  **obalił** i migracja poszła właśnie tam (§19.41.2). ⛔ Nie planować z niego.
* ⏸ **OTWARTE PO M4.3 — żadna z tych pozycji nie jest częścią M4.4, każda ma własny powód:**
  * **B1** — prywatne ikony PK/FK/Unique w `TableDetailTabView` na siatce 14 zamiast 24; przeniesienie do
    systemu = **zmiana wyglądu**, więc czeka na decyzję wizualną użytkownika (§19.40.3). To 5 z 14 literałów
    sufitu ikon.
  * **ogon literałów ikon 10/11/13/15** — pytanie o ROLE, nie o gęstość (§19.37.7).
  * **migracja odstępów** (`Spacing`/`Padding`/`Margin`) — **osobny etap PO M4.4**, ratyfikowany; dziś działa
    wyłącznie zapadka, **⛔ nie zmieniać wartości, żeby ją zadowolić** (§19.39.4).
  * **`FontFamily`** (7 stringów / 95 wystąpień / 33 pliki) — backlog sprintu UX, nie M4.
  * **`GridSplitter Height="4"` ×5** — element chromy dzielony z M4.4; rozstrzyga się raz, na komplecie (R7).
    ⭐ **To jedyna z tych pozycji, którą M4.4 może naturalnie napotkać** — wtedy decyzja w kontekście zmiany
    (D‑M4‑3), a nie z góry.
  * **sieroty po §13.3 poza M4.3** — ~43 wystąpienia w `src/`, częściowo już rozstrzygnięte i zapisane
    historycznie; przed ogłoszeniem czegokolwiek „zamkniętym" trzeba je rozdzielić na żywe i historyczne (#340).
* **Z‑3 (wiersz Table Data)** — ⛔ najpierw PRZYCZYNA. Zmierzone: `data-edit` deklaruje stałe
  `{DynamicResource Size.Row.GridEdit}` (30), a **liczby 40 px nie ma nigdzie w `src/`**; wymaga pomiaru na
  żywej aplikacji (skala DPI? inny element?), nie projektowania.
* **R‑6 / 150 % DPI** — sprawdzić okiem po etapie, który rusza metryki. Oba bloki M4 ruszały; QA użytkownika
  ich nie obejmowało.

---

## 6. Obowiązkowa kolejność zamykania iteracji

1. `dotnet build EmberTern.slnx` → **0/0**;
2. **trzy partycje** testów (filtr w `CLAUDE.md` → „Tests"); ⚠ `ConnectionExpandBindingProbe`
   + `BrandingPresentationTests` **osobno**;
3. **smoke** — aplikacja startuje;
4. **150 % DPI na oko**, jeśli iteracja ruszała metrykę;
5. **QA wizualne użytkownika w obu motywach** — to ono zamyka iterację, nie zielone testy (§0.1.1);
6. dokumentacja **w tej samej iteracji**: as-built w `product-polish.md`, gotcha w `docs/gotchas.md`,
   „Current state" w `CLAUDE.md` **w miejscu**, i ⚠ **przelicz liczbę testów**;
7. commit; po akceptacji **push na OBA remote'y** (`origin` i `private`), potem weryfikacja SHA.

⭐ **Nowy strażnik weryfikuje się PODSADZENIEM NARUSZENIA** — to jedyny krok, który ujawnia strażnika
mierzącego co innego, niż mówi jego nazwa (#315). W M4 wyłapał tak trzy błędy w moich własnych testach.
⚠ **Czytaj `Liczba błędów: 0` PRZED listą niepowodzeń** — inaczej testy biegną na starym binarium (§19.31).
