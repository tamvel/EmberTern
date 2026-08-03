# EmberTern — PROMPT STARTOWY: M3.2d i dalej (pasek zakładek, Metadata Explorer, brama §13.3)

> Wklej to jako pierwszą wiadomość nowej sesji. Dokument jest **samowystarczalny w zakresie stanu,
> decyzji i planu** — do implementacji sięgniesz jeszcze po dokumenty wskazane w §1.

---

## 0. Jednozdaniowe streszczenie poprzedniej sesji

**Język kolorów został zaprojektowany, zaakceptowany, wdrożony w całym produkcie i odebrany wizualnie**
(K1–K7 + przegląd domykający + poprawka optyczna paska statusu) — ⭐ **nie ma tam nic do dokończenia**,
a wyniósł z siebie trzy nowe reguły (**R15, R16, R17**) i cztery pułapki (**18–21**), które obowiązują
od teraz w całym projekcie.

---

## 1. Co przeczytać, zanim napiszesz linijkę kodu

| # | Dokument | Zakres |
|---|---|---|
| 1 | **ten plik** | w całości |
| 2 | ⭐⭐ **`docs/design/product-polish-m3-handover.md`** | **w całości** — stan · reguły **R1–R17** · procedura iteracji · **21 pułapek** · plan §10 |
| 3 | `product-polish.md` **§8** | model paska zakładek (§8.0–§8.3) — to jest zakres M3.3 |
| 4 | `product-polish.md` **§19.20** | podsumowanie zamykające język kolorów: co dostarczono, R15–R17, pułapki 18–21 |
| 5 | `docs/design/color-language.md` | **tylko gdy dotykasz koloru** — §6 (jaką rolę ma nowa akcja) i ⛔ **§0.5** (bramka nadrzędna) |

⛔ **Nie czytaj na starcie:** `product-polish.md` §15, §18.x, §19.0–§19.19 (sięgaj po konkretną
podsekcję, gdy dotyczy tego, co robisz) · handoverów M2a/M2b/M2c.

---

## 2. Stan projektu

| | |
|---|---|
| **Branch** | `feat/product-polish` |
| **Ostatni commit** | `2839d22` — ⭐ **drzewo czyste, oba remote'y (`origin` + `private`) zsynchronizowane, NIC nie czeka na push** |
| **Build** | 0 błędów / 0 ostrzeżeń |
| **Suite** | **7138** zielony w trzech partycjach (**7032 + 52 + 54**) |
| **Smoke** | czysty |
| **Etap** | M0–M2c ✅ · M3: iteracja 0 ✅ · M3.1a–M3.1f ✅ · M3.2a ✅ · M3.2b ⛔ wycofana · **🔒 język kolorów ✅ WDROŻONY I ODEBRANY** |
| ⭐⭐ **START** | **M3.2d — M‑1: 10 literałów → `UiStrings`** (pozycja 11 w planie §10 handovera) |

### 2.1 Co jest zamknięte i nie wraca

**M3.1** (Status Bar 2.0, sześć iteracji) · **H‑3** (stabilny układ paska tytułu) · **H‑5**
(Commit/Rollback na własnych tokenach) · **§7.5** (zastąpione przez `color-language.md`) ·
**wszystkie pytania O‑1…O‑5** języka kolorów.

---

## 3. ⭐⭐ Zadanie: M3.2d — M‑1

> Krok **czysto porządkowy, ZERO zmian wizualnych.** Dobry na rozgrzewkę po zamknięciu języka.

| Gdzie | Ile | Uwaga |
|---|---|---|
| `MainWindow.axaml` — toolbar połączeń | **7** | `ToolTip.Tip="Edit Connection"`, `"Copy Connection"`, `"Delete Connection"`, `"Connect"`, `"Disconnect"`, `"Reconnect"` i sąsiedzi — literały zamiast `x:Static app:UiStrings.*` |
| `MainWindow.axaml` — przyciski okna | **3** | Minimize / Maximize / Close |
| ⛔ `PerformancePanelView`, `SessionManagerTabView` | 2 | **poza M3** — M4.3 |

⚠ **Zmierzone w iteracji 0** (handover §3.7): audyt przypisał M‑1 w całości do M3.2; realnie **10 trafia
tutaj, 1 do M3.3** („Close tab"), **2 zostają poza etapem**.

**Definicja ukończenia:** żaden z tych 10 literałów nie zostaje w XAML · nowe stałe w `UiStrings`
nazwane spójnie z sąsiadami · build 0/0 · trzy partycje zielone · smoke · commit.

⚠ **Nie dopisuj skrótu klawiszowego do tekstu tooltipa ręcznie** — jeśli któryś z tych przycisków ma
gest, komponuje go `CommandTip` z katalogu komend (gotcha **#284**: gest wpisany ręcznie starzeje się
po cichu, przy zielonym buildzie).

---

## 4. Plan po M3.2d

| # | Podetap | Zakres | Decyzja |
|---|---|---|---|
| 12 | **M3.3a** | Pasek zakładek — geometria, `Size.Row.Tab`, wskaźnik. ⛔ **K9/K10 zostają** w rejestrze kolizji | — |
| 13 | **M3.3b** | Dwa tryby + preferencje (`TabStripMode`, `TabStripMaxRows`) + wiersze w Settings Center | ⚠ preferencje |
| 14 | **M3.3c** | Menu kontekstowe zakładki — 8 pozycji, **czwarte wejście do bramki** Save/Discard/Cancel | — |
| 15 | **M3.4a** | Metadata Explorer — wiersz drzewa | **DB** (wiersz **zostaje 24**) |
| 16 | **M3.4b** | Przegląd menu kontekstowych | — |
| 17 | **M3b** | Podłączenie pozostałych operacji do paska postępu (16 VM, 3 ścieżki `IProgress`) + ⏸ pełna semantyka kolorów railu | — |
| 18 | ⛔ **brama §13.3** | **Cztery powierzchnie JEDNOCZEŚNIE**, żywa baza, oba motywy | — |
| 19 | — | Podsumowanie zamykające M3 + handover M4 + prompt startowy | — |

⭐ **Brama §13.3 zyskała na wadze przez R17:** przegląd domykający języka pokazał, że **dwie
pozostałości stały się rozstrzygalne dopiero, gdy patrzyło się na cały pasek naraz** — obie wcześniej
odłożone jako „nie wiadomo".

---

## 5. ⭐ Reguły, które przyszły z ostatniej sesji

> Pełna lista R1–R17 jest w handoverze §5. Tu tylko trzy nowe, bo są świeże i zmieniają sposób pracy.

| # | Reguła |
|---|---|
| **R15** | ⭐⭐ **Wielkość iteracji idzie za NIEPEWNOŚCIĄ, nie za ostrożnością.** Drobne kroki, dopóki projekt się formuje; jeden przebieg, gdy jest zaakceptowany. ⚠ Utrzymywanie mikro‑iteracji po ustaniu niepewności jest **własnym trybem porażki** |
| **R16** | ⭐⭐ **Pomiar jest narzędziem DIAGNOSTYCZNYM; kryterium odbioru jest ekran.** ⛔ **Test, który świeci na zielono przy złym wyglądzie, jest GORSZY niż brak testu** — należy go **zawęzić** do tego, o czym maszyna ma coś sensownego do powiedzenia, nie „wzmacniać" |
| **R17** | ⭐ **Zgodność z dokumentem ≠ spójność produktu.** Przegląd całej powierzchni jest **osobnym krokiem**, nigdy sumą odbiorów pojedynczych iteracji |

### 5.1 Tryb pracy dla tego etapu (wynika z R15)

* **M3.2d, M3.3a** — zamknięty zakres ⇒ **jedna iteracja każdy, bez zatrzymań po drodze**.
* **M3.3b, M3.4a** — niosą decyzje (preferencje, **DB**) ⇒ **propozycja przed implementacją**.
* **Powrót do użytkownika** tylko gdy: dokument nie rozstrzyga · realny konflikt projektowy · zmiana
  pogorszyłaby produkt mimo zgodności z dokumentem.

---

## 6. ⚠ Cztery pułapki z ostatniej sesji (pełna lista: handover §9)

| # | Pułapka |
|---|---|
| **18** | ⭐⭐ **Pudełko to nie farba.** Wysokość `TextBlocka` to INTERLINIA; tekst bez znaków schodzących zostawia dolną część pudełka pustą, więc wyrównanie pudełek zostawia widoczny rozjazd. Korekta **przez `RenderTransform`**, wartość całkowita. ⚠ `UseLayoutRounding="False"` jest dla elementu, **który JEST swoją farbą** (koło, tło) — na elemencie z tekstem w środku **pogarsza** |
| **19** | ⭐ **Pomiar po nośniku nie odróżnia roli od stanu.** Inwentarz liczył ikony po tokenie, więc glif stanu trafił do tabeli akcji — trzy wiersze planu nie przetrwały sprawdzenia |
| **20** | ⭐ **Przeczytaj ZAKRES wcześniejszego pomiaru, zanim użyjesz go jako odpowiedzi.** Im bardziej stanowczy komentarz, tym większa pokusa uznania tematu za zamknięty |
| **21** | ⚠ **Nieaktualny komentarz uczy nieprawdy tak samo jak nieaktualny string** — build go nie sprawdzi. Zmieniasz regułę → poszukaj miejsc, które opisują ją prozą |

---

## 7. Obowiązkowa kolejność

```
analiza → (propozycja + AKCEPTACJA, jeśli krok niesie decyzję) → implementacja
  → uruchomienie aplikacji + QA w obu motywach
    → dotnet build (0/0)
      → dotnet test (TRZY partycje, OSOBNO)
        → smoke
          → dokumentacja (product-polish.md §19 + handover)
            → commit (kod + opis iteracji razem)
              → push na oba remote'y — WYŁĄCZNIE po akceptacji użytkownika
```

⚠⚠ **Nigdy nie łącz `dotnet build` i `dotnet test` w jednym poleceniu — deadlock.**
⚠ **Zabij `EmberTern.exe` przed przebudową** — inaczej MSB3021/MSB3027.

**Trzy partycje** (⚠ `ConnectionExpandBindingProbe` biegnie **sam**):

```
--filter "FullyQualifiedName!~ConnectionExpandBindingProbe&FullyQualifiedName!~SettingsCenterViewTests&FullyQualifiedName!~BrandingPresentationTests&FullyQualifiedName!~DesignTokenApplicationTests&FullyQualifiedName!~TabStripPresentationTests"
```

oraz odwrotność z `|`, oraz `ConnectionExpandBindingProbe` osobno. Stan: **7032 + 52 + 54 = 7138**.
⚠⚠ **Filtr jest listą nazw i starzeje się cicho** — kryterium: *czy klasa konstruuje kontrolki
Avalonii*. Jeśli tak, dopisz ją.

---

## 8. ⛔ Czego nie wolno

1. ⛔ **Nie projektuj języka kolorów ponownie i nie „dokańczaj" go** — jest wdrożony w całości.
2. ⛔ **Nie zmieniaj koloru bez przejścia bramki `color-language.md` §0.5** („czy użytkownik rozpozna
   akcję SZYBCIEJ?"; „nie" albo „nie wiadomo" ⇒ zatrzymaj się i wróć z propozycją).
3. ⛔ **Nie rozszerzaj katalogu tokenów, żeby domknąć kolizję** — **K1–K11** czekają na §13.3, gdzie
   ogląda się je wszystkie naraz (R3).
4. ⛔ **Nie likwiduj `AccentIconBrush` / `InfoIconBrush`** — decyzja **DC**, M4.3/M5; ⚠ oba **mają
   konsumentów** (chip debuggera, żarówka Quick Fix, `DebuggerIcon`, Comment).
5. ⛔ **Nie ujednolicaj menu kontekstowych z przyciskami** — osobny, już spójny system.
6. ⛔ **Nie rezerwuj miejsca na element, którego w danym kontekście nie będzie** (**R13**).
7. ⛔ **Nie naprawiaj przy okazji rzeczy spoza zakresu** — zmierz, opisz, zapisz, nie rozwiązuj bez
   decyzji.
8. ⛔ **Nie doprowadzaj reguły do logicznej konsekwencji** (**pułapka 17**): reguła opisuje to, co już
   jest dobre; element niezgodny bywa wyjątkiem, który **działa**.

---

## 9. ⏸ Otwarte pozycje całego etapu

| # | Co | Gdzie rozstrzygane |
|---|---|---|
| **DC** | likwidacja `AccentIconBrush` / `InfoIconBrush` | M4.3 / M5 |
| **K1–K11** | rejestr kolizji katalogu (§18.R) | brama **§13.3** |
| **V‑1** | kontrast koloru komentarzy SQL (4,14:1) — **ratyfikowany, że zostaje** | rewizja w normalnym użyciu |
| **R‑6 (DPI)** | ⚠ częściowo **NIEWERYFIKOWALNE headlessowo** — sprawdzić **150 % okiem** | brama §13.3 |
| ⏸ | pełna semantyka kolorów railu | **M3b** |

---

## 10. ⭐⭐ Reguła prowadząca

> **Użytkownik:** *„Dokument ma prowadzić produkt. Nie produkt dokument."*
> **Użytkownik:** *„Pomiar nadal jest obowiązkowy. Ale nie jest już celem. Jest tylko narzędziem."*
> **Użytkownik (R16):** *„Potraktuj pomiary jako narzędzie diagnostyczne, a nie kryterium zakończenia
> zadania. Kryterium odbioru jest wygląd na ekranie."*

Jeżeli znajdziesz rozwiązanie wyraźnie lepsze od zapisanego:
**propozycja → akceptacja → aktualizacja dokumentu → implementacja.**
⛔ Ani ciche odstępstwo, ani ślepa zgodność. ⛔ Decyzje D1–D12 i reguły R1–R17 zmienia wyłącznie
użytkownik.
