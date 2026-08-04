# EmberTern — PROMPT STARTOWY: M3.4 (Metadata Explorer) i domknięcie M3

> Wklej to jako pierwszą wiadomość nowej sesji. Dokument jest **samowystarczalny w zakresie stanu,
> decyzji i planu** — do implementacji sięgniesz jeszcze po dokumenty wskazane w §1.

---

## 0. Jednozdaniowe streszczenie poprzedniej sesji

**M3.3 (pasek zakładek) zamknięte i odebrane** — trzy podetapy: M3.3a domknął dług techniczny paska
(12 → 5 wartości lokalnych), M3.3b dał **dwa tryby + dwie preferencje + kategorię Tabs** w Settings
Center, M3.3c dołożył **menu kontekstowe** i rozszerzył bramkę reguły #11 z trzech wejść do czterech.
Wcześniej w tej samej sesji: **M3.2d** domknęło M‑1 (literały tooltipów). Suite **7243**, drzewo czyste,
oba remote'y zsynchronizowane.

---

## 1. Co przeczytać, zanim napiszesz linijkę kodu

| # | Dokument | Zakres |
|---|---|---|
| 1 | **ten plik** | w całości |
| 2 | ⭐⭐ **`docs/design/product-polish-m3-handover.md`** | **w całości** — stan · reguły **R1–R17** · procedura iteracji · **21 pułapek** · plan §10 |
| 3 | `product-polish.md` **§0.1** i **§13.3** | dlaczego Metadata Explorer bije ekrany otwierane raz dziennie + brama, która czeka za M3.4 |
| 4 | `product-polish.md` **§19.25** | podsumowanie zamykające M3.3 — cztery ustalenia, które przeżywają ten podetap |
| 5 | `docs/design/metadata-refresh-analysis.md` **§7** | ⚠ **as-built drzewa** — co już zoptymalizowano (Layer 1) i co ZOSTAJE otwarte |
| 6 | `docs/design/color-language.md` | **tylko gdy dotykasz koloru** — §6 i ⛔ **§0.5** (bramka nadrzędna) |

⛔ **Nie czytaj na starcie:** `product-polish.md` §15, §18.x, §19.0–§19.24 (sięgaj po konkretną
podsekcję) · handoverów M2a/M2b/M2c.

---

## 2. Stan projektu

| | |
|---|---|
| **Branch** | `feat/product-polish` |
| **Ostatni commit** | domknięcie dokumentacyjne M3.3. ⚠ **Sprawdź `git log --oneline -1` i `git status`** zamiast wierzyć temu wierszowi |
| **Build** | 0 błędów / 0 ostrzeżeń |
| **Suite** | **7243** zielony w trzech partycjach (**7132 + 57 + 54**). ⚠⚠ **Zmierz przed cytowaniem** — ta liczba była w tych dokumentach błędna już dwa razy |
| **Smoke** | czysty |
| **Etap** | M0–M2c ✅ · M3: iteracja 0 ✅ · **M3.1 ✅ · M3.2 ✅ · 🔒 język kolorów ✅ · 🔒 M3.3 ✅** |
| ⭐⭐ **START** | **M3.4a — Metadata Explorer, wiersz drzewa** (pozycja 15 w planie §10 handovera) |
| ⚠⚠ **CHECKLISTA M3.4** | Trzy pozycje dołożone przez użytkownika przed startem (**§3.3** + handover §3.7a): rzadkie **zawieszenie drzewa** przy rozwijaniu dużej kategorii · pytanie, **czy dzieli mechanizm z zawieszającym się testem** · **krótki przegląd wydajności** rozwijania. ⭐ Jest już zmierzony kandydat na mechanizm — przeczytaj §3.3(a) **zanim** zaczniesz cokolwiek zmieniać |

### 2.1 Co jest zamknięte i nie wraca

**M3.1** (Status Bar 2.0) · **M3.2** (toolbar, H‑3, H‑5) · **🔒 język kolorów** (wdrożony w całym
produkcie) · **🔒 M3.3** (pasek zakładek — trzy podetapy) · **M‑1** (zostały 2 literały, oba w M4.3) ·
wszystkie pytania **O‑1…O‑5** · decyzje **DA–DD**.

---

## 3. ⭐⭐ Zadanie: M3.4a — wiersz drzewa Metadata Explorera

> ⭐ **§0.1 stawia tę powierzchnię bardzo wysoko** — użytkownik patrzy na nią cały dzień pracy.
> ⚠ To iteracja **zmieniająca wygląd**, więc krok 5 procedury (uruchom aplikację i obejrzyj w obu
> motywach) jest **bramką odbioru, nie formalnością** (pułapka 15).

### 3.1 ⛔ Decyzja DB jest już rozstrzygnięta: **wiersz ZOSTAJE 24**

`Size.Row.Tree` deklaruje **20**, rzeczywistość to **24** (`ListBoxItem.MinHeight`). Katalog jest tu
**zamiarem, nie opisem** (pułapka 3 — trzeci raz w tym etapie). Zejście do 20 zmieniłoby gęstość
**najgęstszego widoku aplikacji**, a to jest zmiana produktowa wymagająca oka użytkownika, nie
porządkowanie.

⭐ **Konsekwencja: to token idzie za produktem, nie produkt za tokenem** — dokładnie tak, jak R12
i reguła prowadząca (§11) każą. Zaproponuj poprawienie **katalogu** (20 → 24) z zapisem powodu,
a nie ściśnięcie drzewa.

### 3.2 Co zmierzyć PRZED propozycją

⚠⚠ **Sprawdź w KODZIE, czy przedmiot podetapu jeszcze istnieje** — M3.3a wszedł z zakresem, który
M3.1a już dostarczyła (§19.22.1). Konkretnie zmierz:

* ile realnie mierzy wiersz drzewa i **skąd** bierze wysokość (styl? wartość lokalna? szablon?),
* czy `Size.Row.Tree` ma **jakiegokolwiek konsumenta** (w M3.1a nie miał ani jednego),
* jakie wartości lokalne niesie szablon wiersza (ikona, etykieta, chevron, odstępy),
* czy `Size.Icon` (14) jest tam literałem — od M3.3a ta rola ma **jednego** konsumenta przy **64**
  literałach w aplikacji; drzewo jest naturalnym drugim, ale ⛔ **sweep app‑wide to nie ten etap**.

### 3.3 ⚠⚠ TRZY DODATKOWE POZYCJE CHECKLISTY — zgłoszone przez użytkownika przed startem

> Pełny zapis: **handover §3.7a**. ⭐ To **nie jest nowe wymaganie funkcjonalne**, tylko checklista do
> przejścia **przy okazji**. ⛔ Nie zamieniać w osobny etap, nie naprawiać „w ciemno".

**(a) 🐞 Rzadkie zawieszenie drzewa.** Rozwinięcie **dużej** kategorii → drzewo **samo przewija się
w dół** → aplikacja zawiesza się i zamyka. Zaobserwowane **2–3 razy przez cały okres używania**, więc
**nie** z Product Polish i bardzo trudne do odtworzenia.

⭐⭐ **Jest zmierzony kandydat na mechanizm, znaleziony przy zamykaniu M3.3:**
`SidebarFlatController.OnExpandedChanged` wstawia dzieci **pojedynczo** (`Rows.Insert` w pętli),
a strażnik zbiorczy tej ścieżki **nie obejmuje — pomija ją** (`if (_suspendDepth > 0) return;`). Czyli
rozwinięcie *z kodu* idzie pod strażnikiem (to naprawiła Layer 1), ale rozwinięcie **kliknięciem** na
już załadowanej kategorii robi **N pojedynczych `Insert`ów** do kolekcji związanej z wirtualizującym
`ListBox`em.

⚠⚠ **Uwaga na skalę — to NIE jest to samo Θ(N²), co defekt z Layer 1.** Tutaj jest **Θ(N) powiadomień**
(po jednym na liść) plus **Θ(N × ogon)** przesunięć w `List<T>` pod spodem. Czyli **taniej niż przed
Layer 1, ale nieporównanie drożej niż jedna `Rebuild`** pod strażnikiem. ⭐ **Tej ścieżki nikt jeszcze nie
zmierzył** — Layer 1 mierzył *odświeżanie*, nie *rozwijanie kliknięciem*.
⭐ **Pierwszy krok: ZMIERZ ją** (`tools/probes/MetadataPerfProbe` ma schemat 2 400 tabel) — przed
jakąkolwiek zmianą. ⛔ Nie „naprawiaj" wcześniej: może się okazać, że koszt jest pomijalny, a przyczyna
leży gdzie indziej (kotwiczenie przewijania, `Dispatcher.Post` w `OnIsExpandedChanged`).

**(b) ⚠ Skojarzenie: czy to ten sam mechanizm, co zawieszający się test?** Użytkownik prosi wprost,
żeby **nie zakładać**, że tak, ale sprawdzić. **ZA:** klasa nazywa się `ConnectionExpandBindingProbe`,
a jej `AutoExpandOnConnect_ReflectedInFlatList` ćwiczy dokładnie tę ścieżkę. **PRZECIW:** zmierzono
(Keyboard Manager etap 5), że nazwa testu raportowanego przy zawieszeniu **całej suity jest
POZYCYJNA**, a podejrzanym jest **teardown sesji**. ⭐ **To dwie różne obserwacje.**
⭐⭐ **Test rozstrzygający:** jeśli mechanizmem jest inkrementalny splice, to wymuszenie rozwinięcia
dużej kategorii w teście headless powinno odtworzyć zawieszenie **deterministycznie** — wtedy test
przestaje być „feleryczny" i staje się **regresyjnym testem prawdziwego defektu**. Jeśli nie odtworzy,
hipoteza upada i **też to zapisz**.

**(c) ⚠ Krótki przegląd wydajności rozwijania.** Czy przy ładowaniu kategorii nie ma zbędnej pracy albo
taniego usprawnienia architektonicznego. ⛔ **Nic na siłę** — brak znaleziska jest poprawnym wynikiem.
⚠ Nie mylić z **Layer 2/3** (`metadata-refresh-analysis.md`) — to osobny etap po M3.

### 3.4 Po M3.4a

**M3.4b** — przegląd menu kontekstowych. ⭐ Menu zakładki z M3.3c jest świeżym punktem odniesienia:
ikony przez `{app:MenuIcon}`, gesty przez `{app:CommandGesture}`, **każda pozycja z własnym
`CanExecute`**. Sprawdź, czy 32 istniejące menu trzymają ten sam poziom.

---

## 4. Plan po M3.4

| # | Podetap | Zakres | Decyzja |
|---|---|---|---|
| ✅ 11–14 | **M3.2d, M3.3a–c** | **ZROBIONE** (§19.21–§19.25) | ✅ |
| ⭐⭐ 15 | **M3.4a** | **← TU ZACZYNASZ.** Metadata Explorer — wiersz drzewa | **DB** ✅ (zostaje 24) |
| 16 | **M3.4b** | Przegląd menu kontekstowych | — |
| 17 | **M3b** | Podłączenie pozostałych operacji do paska postępu (16 VM, 3 ścieżki `IProgress`) + ⏸ pełna semantyka kolorów railu | — |
| 18 | ⛔ **brama §13.3** | **Cztery powierzchnie JEDNOCZEŚNIE**, żywa baza, oba motywy | — |
| 19 | — | Podsumowanie zamykające M3 + handover M4 + prompt startowy | — |

⛔ **§13.3 blokuje M4** i po M3.3 waży jeszcze więcej: rejestr kolizji urósł do **K1–K14**, a **K12–K14**
idą tam **jako jedno pytanie o gęstość paska zakładek**, nie jako trzy osobne.

---

## 5. ⭐ Reguły, które wyniosło M3.3

> Pełna lista R1–R17 jest w handoverze §5. Tu cztery ustalenia z ostatniej sesji — świeże i wszystkie
> szersze niż pasek zakładek.

| # | Ustalenie |
|---|---|
| 1 | ⭐⭐ **Zmiana MIEJSCA reguły jest zmianą jej PRIORYTETU.** Ten sam styl w `Border.Styles` i w arkuszu globalnym zachowuje się inaczej wobec wartości lokalnej. ⛔ *„Przeniosłem styl bez zmian"* — zdanie zakazane bez pomiaru |
| 2 | ⭐⭐ **Narzędzie, które liczy RAZ, nie może orzec o ZBIEŻNOŚCI.** Sonda wizualna renderuje jeden przebieg układu; defekt ze sprzężenia zwrotnego jest poza jej zasięgiem **z konstrukcji**, nie przez pomyłkę |
| 3 | ⭐⭐ **Plan etapu starzeje się tak cicho jak string i jak komentarz.** Przed każdym podetapem sprawdź w kodzie, czy jego przedmiot jeszcze istnieje |
| 4 | ⭐⭐ **Test na WARTOŚĆ właściwości nie jest testem na DZIAŁANIE ekranu.** Wiązanie odpytuje ją wyłącznie po `PropertyChanged` — notyfikacja musi być **asercją**, zweryfikowaną podłożeniem naruszenia (**R16**) |

---

## 6. ⚠ Pułapki najgroźniejsze dla M3.4 (pełna lista: handover §9)

1. ⚠⚠ **`{DynamicResource}` NIE rzuca przy brakującym kluczu** — literówka jest niewidoczna przy zielonym
   buildzie. Nazwy ról bierz z `Tokens.axaml`, nie z pamięci.
2. ⚠⚠ **Wartość lokalna bije setter stylu.** Po jej usunięciu kontrolka zaczyna słuchać systemu i może
   wyglądać inaczej — to **ujawniony dług**, nie regresja. Zgłoś, nie maskuj.
3. ⚠⚠ **Katalog bywa zamiarem, nie opisem** — w M3 obalony **trzy razy** (`Size.StatusBar`, `Size.Row.Tab`,
   `Pad.Tab`/`Size.Icon`). `Size.Row.Tree` jest czwartym przypadkiem i **wiadomo o tym z góry**.
4. ⚠⚠ **Test headless konstruujący `MainWindow` ZAWIESZA suite.** Asercje rób na najtańszej kontrolce
   (`new Window()`); nowa klasa headless **dołącza do `HeadlessCollection`** i **do filtra partycji**.
5. ⚠ **Drzewo jest najgęstszym widokiem aplikacji** — każda zmiana odstępu mnoży się przez tysiące wierszy.
   ⛔ To sprawia, że „drobna korekta" tutaj nie jest drobna.
6. ⭐ **Pomiar po nośniku nie odróżnia roli od stanu** (pułapka 19) — i rejestr K9/K10 dał się na to nabrać,
   bo „zakładka" znaczyła dwie różne rzeczy. W drzewie tym słowem jest **„wiersz"**.

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
--filter "FullyQualifiedName!~ConnectionExpandBindingProbe&FullyQualifiedName!~SettingsCenterViewTests&FullyQualifiedName!~BrandingPresentationTests&FullyQualifiedName!~DesignTokenApplicationTests&FullyQualifiedName!~TabStripPresentationTests&FullyQualifiedName!~MetadataTreeVirtualizationProbe"
```

oraz odwrotność z `|`, oraz `ConnectionExpandBindingProbe` osobno. Stan: **7132 + 57 + 54 = 7243**.
⚠⚠ **Filtr jest listą nazw i starzeje się cicho** — kryterium: *czy klasa konstruuje kontrolki Avalonii*.

---

## 8. ⛔ Czego nie wolno

1. ⛔ **Nie wracaj do paska zakładek** bez realnego defektu funkcjonalnego — M3.3 zamknięte i odebrane.
2. ⛔ **Nie projektuj języka kolorów ponownie**; przy każdej zmianie koloru przejdź bramkę `color-language.md` **§0.5**.
3. ⛔ **Nie rozszerzaj katalogu, żeby domknąć kolizję** — **K1–K14** czekają na §13.3 (R3).
4. ⛔ **Nie rób sweepu `Size.Icon` app‑wide** (64 literały) — to §13.3/M4.3, nie ten etap.
5. ⛔ **Nie migruj 18 drzew „Zależności"** — M4.2b, i **nigdy na `SidebarFlatController`**.
6. ⛔ **Nie zmieniaj Metadata Explorera na inny komponent** — D10, płaski kontroler jest docelowy.
7. ⛔ **Nie ruszaj Layer 2/3 z `metadata-refresh-analysis.md`** — to osobny etap wydajnościowy po M3.
8. ⛔ **Nie rezerwuj miejsca na element, którego w danym kontekście nie będzie** (**R13**).
9. ⛔ **Nie doprowadzaj reguły do logicznej konsekwencji** (**pułapka 17**) — element niezgodny bywa
   wyjątkiem, który **działa**.
10. ⛔ **Nie naprawiaj przy okazji rzeczy spoza zakresu** — zmierz, opisz, zapisz, nie rozwiązuj bez decyzji.

---

## 9. ⏸ Otwarte pozycje całego etapu

| # | Co | Gdzie rozstrzygane |
|---|---|---|
| **DC** | likwidacja `AccentIconBrush` / `InfoIconBrush` (24 wystąpienia / 14 plików) | M4.3 / M5 |
| **K1–K14** | rejestr kolizji (§18.R); ⭐ **K12–K14 jako JEDNO pytanie o gęstość** | brama **§13.3** |
| **V‑1** | kontrast koloru komentarzy SQL (4,14:1) — ratyfikowany, że zostaje | rewizja w użyciu |
| **R‑6 (DPI)** | ⚠ częściowo **nieweryfikowalne headlessowo** — sprawdzić **150 % okiem** | brama §13.3 |
| ⏸ | przycisk/licznik przepełnienia paska zakładek (§8.2) | do zaplanowania |
| ⏸ | 6 sierocych stałych `UiStrings` · role `Pad.Tab` i `Size.Icon.Lg` bez konsumentów | §13.3 / M4.3 |
| ⏸ | pełna semantyka kolorów railu | **M3b** |

---

## 10. ⭐⭐ Reguła prowadząca

> **Użytkownik:** *„Dokument ma prowadzić produkt. Nie produkt dokument."*
> **Użytkownik (R16):** *„Potraktuj pomiary jako narzędzie diagnostyczne, a nie kryterium zakończenia
> zadania. Kryterium odbioru jest wygląd na ekranie."*

Jeżeli znajdziesz rozwiązanie wyraźnie lepsze od zapisanego:
**propozycja → akceptacja → aktualizacja dokumentu → implementacja.**
⛔ Ani ciche odstępstwo, ani ślepa zgodność. ⛔ Decyzje D1–D12 i reguły R1–R17 zmienia wyłącznie
użytkownik.

⭐ **M3.3 dało trzy dowody, że to działa w praktyce:** przeskalowanie M3.3a (plan był nieaktualny),
ukrycie „Maximum rows" (moja decyzja była gorsza) i strukturalna poprawka paska przewijania
(*„naprawiasz objaw zamiast układu"*). **Za każdym razem wygrała korekta użytkownika, nie obrona
implementacji.**
