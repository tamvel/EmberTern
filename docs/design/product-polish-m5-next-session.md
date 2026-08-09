# Product Polish — prompt startowy PO ZAMKNIĘCIU M4

> ## ⚠⚠ STAN AKTUALNY (2026-08-10): **M5 WYSTARTOWAŁ — ten dokument jest częściowo wykonany**
>
> Użytkownik wybrał **M5 — Final Polish** (§3.1) i zlecił kolejność: najpierw §10, potem dalsze punkty M5.
>
> ✅ **§10 / kontrast severity — ZAIMPLEMENTOWANE, ⏸ CZEKA NA QA WIZUALNE UŻYTKOWNIKA.**
> Commit `feat(m5): kontrast severity…`. Wariant **B** ratyfikowany; trzy wartości w `Colors.axaml`;
> §10 sprostowane (próg 3:1 to **wymóg własny**, nie „WCAG AA Large"); trzy strażniki
> (`SeverityText_*`, `SeveritySignal_*`) zweryfikowane podsadzeniem.
> Zapis: `product-polish.md` **§19.45** · decyzja: **`product-polish-m5-severity-contrast-decision.md`**.
>
> ⛔ **NASTĘPNY KROK TO QA UŻYTKOWNIKA, NIE KOLEJNY TEMAT.** Dopiero po nim rusza kolejna pozycja M5
> (kandydaci w §3.1: DPI 100/125/150/200 · empty states M‑3 · terminologia M‑4 · focus L‑1 · animacje §9).
> ⭐ Zmierzone przy okazji inwentaryzacji, przydatne do planowania kolejnego kroku:
> **§9 nie ma ani jednego naruszenia** (zero `Transitions` w całym `EmberTern.App`), **L‑1 potwierdzone
> otwarte** (`Button.primary`/`.caption` bez `:focus`), a **M‑3 jest znacznie mniejsze, niż mówi audyt**
> (nie „3 z 48", tylko ~12 widoków + kilka ViewModeli; do tego **8 osieroconych stałych `*Empty*`**).
>
> ⚠ Liczby w §1 i §3 niżej pochodzą sprzed M5 — suite to teraz **8351** (8193 + **103** + 55).

> **Do wklejenia na początku następnej sesji.** Zastępuje `product-polish-m4-migration-next-session.md`,
> który jest od 2026-08-09 **historyczny** — czytaj go tylko dla „dlaczego", nigdy dla „co dalej".
>
> 🏁 **M4 JEST ZAMKNIĘTY W CAŁOŚCI I ODEBRANY (2026-08-09).** Oba bloki decyzyjne (gęstość · typografia)
> + pięć etapów migracji (M4.1 · M4.2 · M4.2b · M4.3b+c · M4.4). Rejestr kolizji **K1–K15 zamknięty**,
> **zero otwartych pytań projektowych M4**.
>
> ⛔⛔ **PIERWSZY KROK TEJ SESJI TO DECYZJA UŻYTKOWNIKA, NIE KOD.** Po M4 nie ma jednego oczywistego
> następnego etapu — są **trzy kandydatury o różnym charakterze** (§3). Nie zaczynaj żadnej z nich bez
> wskazania. ⛔ Nie zaczynaj też sesji od raportowania stanu: jeżeli `git status` jest czysty, to jest
> cała odpowiedź.

---

## 1. Punkt odniesienia

**`feat/product-polish`**, build 0/0, suite **8345** w trzech partycjach (**8193 + 97 + 55**), smoke czysty,
oba remote'y zsynchronizowane. ⚠ Liczbę testów **zmierz, nie przepisuj** — ta linia dryfowała wielokrotnie
i raz była wewnętrznie sprzeczna (deklarowała sumę inną niż jej własne składniki).

⛔ **Kryterium zieloności to SUMA, nie „0 niepowodzeń"** — run, w którym cała partycja nie wystartowała, też
raportuje zero błędów (zmierzone 2026-08-05).

⚠ **Znany flake, nie ogłaszany naprawionym:** `SettingsLoadHealthTests.ConcurrentSaves_NeverLeaveSettingsUnreadable`
(test na `Parallel.For`) potrafi zaświecić raz na czerwono i przechodzi solo.

---

## 2. ⭐⭐ Co M4 naprawdę dostarczył — i co z tego wynika na przyszłość

### 2.1 Produkt

| blok / etap | as-built | treść |
|---|---|---|
| gęstość | §19.37 | `Size.Icon.Toolbar` (16) / `Size.Icon` (14) — drabina „akcja vs wiersz"; wiersz drzewa; `Size.Row.GridEdit` = 30; podłogi list w imporcie |
| typografia | §19.38 | `Text.SectionHeader` 11 → **12 SemiBold** (nagłówek był mniejszy od tekstu, który nazywa); `Text.Toolbar` wycofana; `Radius.Tab` |
| M4.1 | §19.39 | ikony przy etykiecie; **sufit literałów ikon 95 → 20**; zapadka na odstępy; wyśrodkowanie `Icon.Undo`/`Icon.Redo` |
| M4.2 | §19.40 | edytory obiektów — **pomiar wykazał, że migracja była już wykonana**; `Radius.Surface` na karcie; **B1** ujawnione |
| M4.2b | §19.41 | 17 drzew „Zależności" → jedna kontrolka na `SidebarFlatController`; wspólna kolejność kategorii; nawigacja ←/→ |
| M4.3b+c | §19.42–43 | monitory i debugger na role; **jeden `Button.seg`** zamiast dwóch kopii |
| M4.4 | §19.44 | dialogi i okna; **M‑5** — `GrowingDialogBehavior` z regułą `min` |

**Liczniki po M4** (regeks strażnika, `Views/` + `Controls/`): `FontSize` **28 / 9 plików** · `CornerRadius`
**7 / 3** · literały rozmiaru ikony **14 / 6**. ⭐ To już wyłącznie **uzasadnione wyjątki i arytmetyka**
(koła, kapsuły, resety, glify strojone do kontenera) plus **B1** — a nie „reszta do zrobienia".

### 2.2 ⭐⭐ Wynik metodologiczny — ważniejszy od liczb

**CZTERY zapisane przesłanki nie przeżyły zderzenia z kodem:**

1. „`Size.Icon` — 64 literały" opisywało **164 z 355** deklaracji ikon; 191 nie deklaruje nic i bierze 16
   z `ControlTheme` (**#332**).
2. „K15 — 112 wystąpień w 17 plikach, zmiana rozjechałaby drzewo z resztą aplikacji" — naprawdę **44 w 13**,
   z czego **41 to JEDNA rola**, więc R7 przemawiał *za* zmianą, nie przeciw.
3. §13.2 odrzucało `SidebarFlatController` sprzężeniem *„z połączeniem, metadanymi, filtrowaniem"* —
   jego konstruktor bierze **wyłącznie delegaty** (§19.41.2). Wykrył to **użytkownik z działającej
   aplikacji**, nie ja z dokumentu.
4. „`GridSplitter` to jedyna odłożona pozycja, którą M4.4 napotka" — **w 25 oknach nie ma go ani razu**.

**TRZY ETAPY Z RZĘDU** (M4.2, M4.3, M4.4) okazały się nie sweepem literałów, tylko **odbiorem decyzji,
których nikt nie podjął** — odesłanie „rozstrzyga §13.3" żyje w ŹRÓDLE, rejestr w DOKUMENCIE, a zamykany
bywa wyłącznie dokument (**#340**).

⭐ **Praktyczne wnioski, które przenoszą się na każdy następny sprint:**

* **liczba w prozie starzeje się cicho** — mierz przed planowaniem, nie cytuj;
* **podsadzenie, które NIE zapala testu, jest wynikiem** — wtedy dowiadujesz się, że mierzysz co innego,
  niż deklarujesz (#342 zniósł tak uzasadnienie całej iteracji);
* **grupuj po tym, CZYM element jest dla użytkownika**, nie po nazwie geometrii, wartości ani po tym, że
  właśnie migrował (#335, #341);
* **zero z licznika bywa „nie zbudowano tego tak tutaj", nie „czysto"** (#337);
* ⚠ **spadek licznika bywa PRZENIESIENIEM, nie migracją** — `Measure` skanuje tylko `Views/` + `Controls/`,
  więc wartość przeniesiona do `Themes/` znika z licznika, choć istnieje dalej.

---

## 3. ⏭ TRZY KANDYDATURY NA NASTĘPNY ETAP — wymagają decyzji użytkownika

⚠ Podane zakresy są **zmierzone 2026-08-09**, nie przepisane z planu.

### 3.1 **M5 — Final Polish** (pozycja z planu §13)

Zakres z §13: oba motywy · kontrast §10 · **DPI 100/125/150/200** · empty states (**M‑3**) · terminologia
i słownik (**M‑4**) · focus (**L‑1**) · animacje (**§9**).

⭐ **Argument za teraz:** to jedyna pozycja, która domyka *etap* zgodnie z jego własnym planem, i jedyna,
która patrzy na produkt **jako całość** — a §0.1.1 mówi, że kryterium sukcesu jest pierwsze wrażenie, nie
zgodność z katalogiem. ⚠ **R‑6 / 150 % DPI jest zaległe od dwóch bloków M4** — oba ruszały metryki, a QA
użytkownika ich nie obejmowało.

### 3.2 **Etap odstępów** (`Spacing` / `Padding` / `Margin`) — ratyfikowany jako osobny etap PO M4.4

**Zmierzone teraz:** `Spacing` **309 / 46 plików** · `Padding` **185 / 50** · `Margin` **475 / 55** —
razem **969 wartości lokalnych**. Rolę czyta: `Spacing` **11**, `Margin` **16**, `Padding` **0**.

⚠ Dziś działa **wyłącznie zapadka** (per plik), i ⛔ obowiązuje jawne zastrzeżenie: *nie zmieniać żadnej
wartości tylko po to, żeby ją zadowolić*. ⭐ Z M4.1: **214 z 320 `Spacing` pokrywa się 1:1** ze skalą, więc
ta część jest mechaniczna — treścią etapu jest **ogon** (5, 2, 1, 10, 3 px) i pytanie, czy `Padding`/`Margin`
w ogóle mają czytać role, skoro dziś nie czytają ich **ani razu**.
⭐ **Materiał gotowy z M4.4:** nagłówek dialogu ma **`20,16` ×15 vs `20,14` ×5** przy **w pełni spójnej
stopce (19/19 przy `20,12`)**, a rola **`Pad.Dialog` = `20,16`** opisuje większość dokładnie i ma **zero
konsumentów**. To jest gotowy pierwszy przypadek tego etapu.

### 3.3 **App-wide UX sprint** (backlog, odblokowany od zamknięcia Data Import)

Gęstość kontrolek formularzy app-wide + **czcionka monospace**: zmierzone **`FontFamily` 81 wystąpień
w 28 plikach** (dokumentacja mówi o 7 różnych stringach / 95 / 33 — ⚠ **przelicz, zanim zacytujesz**).
⭐ Ten sprint decyduje `Cascadia Code` vs `Cascadia Mono` dla edytora, debuggera, kart hover i 11 podglądów
DDL **naraz** — to decyzja typograficzna dla sprintu, który widzi wszystkie powierzchnie razem.

### 3.4 ⏸ Pozycje odłożone, każda z powodem — **żadna nie jest samodzielnym etapem**

* **B1** — prywatne ikony PK/FK/Unique w `TableDetailTabView` na siatce 14 zamiast kanonicznych 24;
  przeniesienie do systemu = **zmiana wyglądu**, więc czeka na decyzję wizualną (§19.40.3). To **5 z 14**
  literałów sufitu ikon.
* **Z‑3** — wiersz Table Data; ⛔ **najpierw PRZYCZYNA**: zmierzone, że liczby 40 px **nie ma w `src/`**,
  a `data-edit` czyta rolę 30. Wymaga pomiaru na żywej aplikacji (skala DPI? inny element?).
* **ogon literałów ikon 10/11/13/15** — pytanie o ROLE, nie o gęstość (§19.37.7).
* **sieroty po §13.3 poza zakresem M4** — ⚠ **50 wystąpień „13.3" w `src/`, ale to DWIE różne rzeczy**:
  7 to cytat WYMAGANIA („§13.3 specyfikacji", Zero Layout Shift), reszta to odesłania i zapisy historyczne.
  ⛔ Przed ogłoszeniem czegokolwiek „zamkniętym" trzeba je rozdzielić **czytając**, nie regeksem (#340).
* **poszerzenie okna licznika `FontSize`** o `Completion/`, `Sql/`, `Themes/` — **29 deklaracji poza
  zasięgiem**; zbudowane i **wycofane**, bo ten sam `Measure` obsługuje `FontFamily` (§19.38.7).

---

## 4. Co przeczytać i w jakiej kolejności

1. **`CLAUDE.md`** — „Current state" (wpis M4 + M4.4) i „UI styling rules" (reguły 1–11, zwłaszcza **#8**
   trzy trasy restylowania, **#9** zakaz literałów tam, gdzie jest rola, **#10** kontener rozstrzyga
   wielkość, **#11** reguła formułowana pozytywnie).
2. **`docs/design/product-polish.md`** — ⛔ nie w całości: **§13** plan + **§13.0** DoD + **§0.1 / §0.1.1 /
   §0.1.2** (zasady nadrzędne wobec katalogu) + as-built etapu, którego dotyczy praca.
3. **`docs/design/product-polish-m3-handover.md`** — **reguły R1–R18** (§5) i **21 pułapek** (§9).
4. **`docs/design/color-language.md`** — **§0.5** przed każdą zmianą koloru, **§6** przy nowej akcji.
5. **`docs/gotchas.md`** — **#340 · #341 · #342 · #343** (świeże, z M4.3/M4.4), **#335**, **#337**, **#322**, **#284**.

---

## 5. ⭐ Reguły, które w M4 decydowały najczęściej

| # | reguła |
|---|---|
| **R7** | nie łatać pojedynczego ekranu, gdy defekt jest app-wide |
| **R8** | kryterium odbioru: *„czy to wygląda jak dopracowana aplikacja komercyjna?"* |
| **R12** | **błędna rola jest gorsza od wartości lokalnej**; celem jest usunięcie wartości NIEUZASADNIONYCH |
| **R16** | pomiar jest narzędziem DIAGNOSTYCZNYM, kryterium odbioru jest ekran; **test zielony na złym ekranie jest gorszy niż brak testu** |
| **R17** | zgodność z dokumentem ≠ spójność produktu |
| **R18** | przy równej czytelności wygrywa wariant **gęstszy** — ⚠ rozstrzyga REMIS, warunek czytelności jest pierwszy |
| **pułapka 17** | reguła OPISUJE to, co już jest dobre; element niezgodny z regułą bywa wyjątkiem, który DZIAŁA |

⛔ **Do NIEOTWIERANIA bez osobnej decyzji:** „tęcza ikon" w pasku narzędzi (§13.3a.3) · pasek zakładek
(M3.3) · menu kontekstowe · Metadata Explorer · `field-label` i jego 164 użycia · 9 px i 12 px w edytorach
stojących w wierszu siatki · ratyfikowane decyzje M4 z §19.37 i §19.38 · **K4 (`PlanLead`) zostaje 13**.

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

⭐ **Nowy strażnik weryfikuje się PODSADZENIEM NARUSZENIA** — w M4 wyłapało to **cztery** błędy w moich
własnych testach, w tym dwa, których czytanie kodu by nie znalazło.
⚠ **Czytaj `Liczba błędów: 0` PRZED listą niepowodzeń** — inaczej testy biegną na starym binarium.
⚠ **Polski cudzysłów otwierający sparowany z ASCII zamykającym** wewnątrz interpolowanego stringa zamyka
literał; ta pułapka wystąpiła w M4.3 i **ponownie w M4.4**.
