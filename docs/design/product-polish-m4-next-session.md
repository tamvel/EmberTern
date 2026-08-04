# Product Polish — prompt startowy na M4

> **Do wklejenia na początku następnej sesji.** Zastępuje `product-polish-m3-next-session.md`, który od
> 2026-08-04 opisuje pracę ZAMKNIĘTĄ (M3 · M3b · brama §13.3 · M3.5) i służy już tylko za zapis „dlaczego".
>
> ⛔⛔ **M4 NIE ZACZYNA SIĘ BEZ WYRAŹNEJ ZGODY UŻYTKOWNIKA.** Ten dokument przygotowuje wejście, nie jest
> zgodą na wejście.

---

## 1. Stan na wejściu — jednym akapitem

Product Polish ma za sobą **M0–M2c** (katalog tokenów, migracja kontrolek bazowych, usunięcie tego, co
katalog zablokował), **M3** (Status Bar 2.0 · język kolorów · pasek zakładek · Metadata Explorer),
**M3b** (podłączenie operacji do sekcji postępu), ⛔ **bramę §13.3** (przegląd czterech powierzchni trwałych
na żywej bazie, w obu motywach — **przeszła**) oraz **M3.5** (trzy defekty, które brama znalazła).

Punkt odniesienia: gałąź `feat/product-polish`, commit **`cb76c0b`** (na obu remote'ach). Build 0/0 ·
suite **7317** w trzech partycjach (7196 + 67 + 54) · smoke czysty.
🔒 **M3.5 odebrane przez użytkownika w obu motywach 2026-08-04** — w tym ratyfikacja architektury
`CreateIcon` (lepsza niż dziewięć wariantów `*Plus`) i proporcji badge'a. ⛔ Nie otwierać ponownie.

⏸ **Otwarte i przypisane, ale ŻADNE nie blokuje M4:** Z‑3 (wiersz Table Data — najpierw przyczyna) ·
Z‑4 (okno Settings ucina wiersz) · Z‑5 (edytor daty w dialogu) · temat **Settings Center jako powierzchnia
UX** · **P‑1** (paleta za uboga na rozróżnianie aktywności railem, z P‑2 i P‑3) · **DC** (likwidacja
`AccentIconBrush`/`InfoIconBrush`) · **R‑6** (DPI 150 % — częściowo nieweryfikowalne headlessowo).

---

## 2. Co przeczytać i w jakiej kolejności

1. **`CLAUDE.md`** — sekcja „Current state" (stan bieżący i co jest zamknięte) + „UI styling rules" (reguły
   1–11, w szczególności **#8** trzy trasy restylowania i **#9** zakaz literałów tam, gdzie jest rola).
2. **`docs/design/product-polish.md`** — ⛔ **nie w całości.** Konkretnie:
   * **§13** — plan całego etapu i zależności (gdzie M4 leży i od czego zależy);
   * **§13.3a** — wynik bramy: sześć odpowiedzi, znaleziska, **§13.3a.3 wycofana „tęcza ikon"**, §13.3a.5 temat Settings;
   * **§19.36** — as-built M3.5 (w tym dwie zamknięte pomiarem drogi w Z‑6);
   * **§18.R** — rejestr kolizji **K1–K15**, bo to jest wejście do M4.3;
   * **§0.1 / §0.1.1 / §0.1.2** — trzy zasady, które stoją NAD katalogiem.
3. **`docs/design/color-language.md`** — ⛔⛔ **§0.5 przed każdą zmianą koloru** oraz **§6** (drzewo decyzyjne
   dla nowej akcji). Reszta to referencja.
4. **`docs/design/product-polish-m3-handover.md`** — **reguły R1–R17** (§5) i **21 pułapek** (§9). Zamknięte
   jako plan, obowiązujące jako reguły.
5. **`docs/gotchas.md`** — **#313–#315** (świeże, z bramy i M3.5), **#308**, **#288**, **#284**.

---

## 3. ⭐ Reguły, które w M3/M3.5 zdecydowały najczęściej

Pełna lista to R1–R17 w handoverze. Te wracały w każdej iteracji:

| # | Reguła |
|---|---|
| **R7** | Nie łataj pojedynczego ekranu, jeśli defekt jest app-wide — łatanie jednego widoku rozjeżdża go z resztą |
| **R8** | Kryterium odbioru: *„czy to wygląda jak dopracowana aplikacja komercyjna?"* |
| **R12** | **Błędna rola jest gorsza od wartości lokalnej** — wartość z powodem widać jako dług, rola udaje, że długu nie ma |
| **R13** | Nie rezerwuj miejsca na element, który w tym kontekście nie może się pojawić |
| **R15** | **Wielkość iteracji idzie za NIEPEWNOŚCIĄ, nie za ostrożnością** — drobne kroki, dopóki projekt się formuje; jeden przebieg, gdy jest przyjęty |
| **R16** | **Pomiar jest narzędziem DIAGNOSTYCZNYM; kryterium odbioru jest ekran.** Test zielony na złym ekranie jest gorszy od braku testu — taki test się ZWĘŻA, nigdy „wzmacnia" |
| **R17** | **Zgodność z dokumentem ≠ spójność produktu** — przegląd całej powierzchni jest osobnym krokiem |
| **pułapka 17** | **Reguła OPISUJE to, co już jest dobre; nie jest mandatem do zmiany wszystkiego, co do niej nie pasuje.** Element niezgodny z regułą bywa wyjątkiem, który DZIAŁA |

⭐ **Pułapka 17 zebrała w M3 największe żniwo** — M3.2b wycofano w całości, a brama §13.3 popełniła ją
ponownie na „tęczy ikon". Przy każdym wniosku „to nie pasuje do reguły" najpierw pytanie: **czy to działa?**

---

## 4. ⭐⭐ Trzy lekcje z bramy i M3.5, których nie ma w regułach

1. **Wrażenie ze zrzutu jest hipotezą, nie znaleziskiem.** W bramie z siedmiu podejrzeń sześć padło po
   pomiarze — a **dwa z nich były błędami samego pomiaru** (skan chybił 2‑pikselowego wskaźnika o jeden
   wiersz; przechwytywanie ekranu zgubiło cały pasek statusu). Zanim zgłosisz defekt: zmierz, i sprawdź
   narzędzie na czymś, o czym wiesz, że jest poprawne.
2. **Podsadzenie naruszenia nie potwierdza, że strażnik działa — ono ujawnia, że strażnik mierzy CO INNEGO.**
   W M3.5 pierwsza wersja testu kontrastu była zielona, kod poprawny i nic w czytaniu jej nie sugerowało luki;
   dopiero zaplanowane podsadzenie pokazało, że test przeszedłby także wtedy, gdyby kontrolka przestała
   używać roli (#315).
3. **Zamykaj drogi pomiarem, nie opinią.** Z‑6 miało trzy kandydatury; dwie zostały **wykluczone
   arytmetycznie** (w 24 jednostkach pełny glif i duży badge w narożniku nie zmieszczą się bez nachodzenia)
   i **strukturalnie** (`SvgIcon` ma jedną `StrokeThickness` dla całej ścieżki, więc nie umie narysować
   znaku gęstszego od glifu). Zapisane w #314, żeby nikt nie chodził tam po raz trzeci.

---

## 5. Co obejmuje M4 — do POTWIERDZENIA z użytkownikiem przed startem

⚠ **Zakres M4 czytaj z `product-polish.md` §13, nie z pamięci tego dokumentu.** Plan etapu opisuje M4 jako
migrację ekranów na przyjętą ramę; rejestr **K1–K15** (§18.R) jest jego wejściem, a **M4.3** ma tam już
przypisane dwa sweepy app-wide:

* **`Size.Icon`** — 64 literały `Width="14"`/`16"` (znalezisko M3.3a);
* **ikona węzła drzewa 15 px + `Spacing` 5** — **112 wystąpień w 17 plikach** (K15, znalezisko M3.4a).

⭐⭐ **Te dwie listy opisują TĘ SAMĄ app-wide decyzję o rozmiarze ikony i najprawdopodobniej trzeba je zadać
użytkownikowi RAZEM** — tak jak K12–K14 poszły na bramę jako jedno pytanie o gęstość paska zakładek.

⛔ **Zanim napiszesz pierwszą linię M4, ustal z użytkownikiem:**
1. Czy M4 zaczyna się od **rejestru K1–K15** (rozstrzygnięcie kolizji), czy od **migracji ekranów**?
2. Czy sweepy `Size.Icon` + K15 idą **jednym pytaniem o gęstość**, czy osobno?
3. Czy któryś z odłożonych tematów (Z‑3, Settings, P‑1) ma wejść **przed** M4, czy zostaje po nim?

---

## 6. ⛔ Czego NIE robić

* ⛔ **nie wracać do „tęczy ikon"** (§13.3a.3 — wycofane z uzasadnieniem);
* ⛔ **nie wracać do paska zakładek** bez realnego defektu funkcjonalnego (M3.3 zamknięte);
* ⛔ **nie wracać do Data Import ani do Metadata Explorera** poza realnym defektem;
* ⛔ **nie poprawiać Z‑3 „pod katalog"** — najpierw przyczyna 40 px; większy wiersz może być świadomą decyzją;
* ⛔ **nie ruszać palety pod rail** (P‑1) bez decyzji użytkownika — a przy zmianie `AccentColor` pamiętać, że
  **para (`AccentBrush`, `PanelBrush`) ma dwóch konsumentów**: rail i wskaźnik aktywnej zakładki, który
  DZIAŁA (`color-language.md` §9.2);
* ⛔ **nie dodawać wariantów `Icon.<Rodzaj>Plus`** — akcja „utwórz" to `CreateIcon` z geometrią plain przez
  referencję (pinowane przez `CreateIconContractTests`);
* ⛔ **nie naprawiać stanów `:disabled` w `FluentBridge.axaml`** — te klucze obsługują warianty, które
  w stanie wyłączonym MAJĄ wyglądać jak przyciski (#313).

---

## 7. Higiena techniczna — obowiązkowa kolejność na końcu każdej iteracji

1. `dotnet build EmberTern.slnx` — **0/0** (`TreatWarningsAsErrors=true`);
2. **trzy partycje testów**, nie jedna — filtr i uzasadnienie w `CLAUDE.md` („Tests"); ⚠ `ConnectionExpandBindingProbe`
   uruchamiać **osobno** (dyrektywa użytkownika 2026-08-01);
3. **smoke** — aplikacja startuje;
4. **150 % DPI na oko** (R‑6), jeśli iteracja ruszała metrykę;
5. **QA wizualne użytkownika w obu motywach** — to ono zamyka iterację, nie zielone testy (§0.1.1);
6. dokumentacja **w tej samej iteracji**: as-built w `product-polish.md`, gotcha w `docs/gotchas.md`,
   „Current state" w `CLAUDE.md` **w miejscu**, i ⚠ **przelicz liczbę testów** — ta linia dryfowała już
   cztery razy;
7. commit, po akceptacji użytkownika **push na OBA remote'y** (`origin` i `private`).
