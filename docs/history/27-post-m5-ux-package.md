# 27 — Pakiet UX po M5 (2026-08-09 → 2026-08-10) — ZAMKNIĘTY W CAŁOŚCI

Sześć zgłoszeń ze zwykłego używania aplikacji, zebranych po zamknięciu Product Polish M5.
Gałąź `feat/product-polish` (⛔ nadal **nie** scalona do `master`).

Zakres całego pakietu, w kolejności ratyfikowanej przez użytkownika:

| # | Temat | Stan |
|---|---|---|
| 1 | Security Manager — ikona zakładki | ✅ zamknięte, odebrane |
| 2 | `Button.primary` — tekst w stanach | ✅ zamknięte, odebrane |
| 3 | Live DDL — kolorowanie na pięciu powierzchniach | ✅ zamknięte, odebrane |
| 4 | Performance — Execution plan (layout + kolorowanie drzewa) | ✅ zamknięte, odebrane |
| 5 | Settings UX | ✅ zamknięte, odebrane |
| 6 | Database Properties (nowa funkcja) | ✅ zamknięte, odebrane |

---

## §1 Faza 0 — rozpoznanie przed jakąkolwiek zmianą

Użytkownik postawił warunek wprost, wynikający z lekcji całego M5: *„dokumentacja i nazwy często nie
odpowiadają aktualnemu kodowi — zanim coś uznasz za defekt, zmierz rzeczywisty kod i zachowanie"*.

⭐⭐ **Trzy z sześciu zgłoszeń opisywały coś innego, niż się wydawało — i w każdym przypadku zmieniło to
kształt pracy, nie tylko jej rozmiar.**

1. **Ikona Security** nie była defektem renderowania, tylko **ratyfikowaną decyzją wykonaną w połowie**.
2. **Plan wykonania** nie potrzebuje parsera — **parser już istnieje i jego wynik jest wyrzucany**.
3. **Live DDL** to nie jedna powierzchnia, tylko **pięć bajtowo identycznych kopii jednego kształtu**.

⚠ Dwie hipotezy użytkownika zostały pomiarem **odrzucone jako złe narzędzie**, nie jako zły cel:
kolorowanie planu przez syntax highlighting SQL (plan Firebirda nie jest SQL-em) oraz założenie, że
naprawa przycisku dotyczy tylko motywu Light.

---

## §2 Punkt 1 — Security Manager: ikona zakładki

### §2.1 Co zgłoszono i co było naprawdę

Zgłoszenie: *„na pasku modułów ikona jest niebieska, a w zakładce biała"*.

Zmierzone: zakładka czytała `MetadataNodeViewModel.ResourceKeyFor(User|Role)` → `IconColor_Role`
= **`#90A4AE`** (Dark) / `#37474F` (Light). Przy 14 px `#90A4AE` czyta się jak biały — więc obserwacja
użytkownika była trafna co do wrażenia i myląca co do przyczyny.

⭐ **Przycisk na pasku niósł w komentarzu zapisaną decyzję** (`MainWindow.axaml:200-205`, domknięcie
K‑final / rola R‑6 „wejście do narzędzia"): był jedynym, który zamiast koloru ROLI (S2) nosił kolor
RODZAJU (S1), i został przeniesiony na `AccentBrush`, z uzasadnieniem *gdy element miałby nieść i rodzaj,
i skutek — wygrywa skutek*. **Zakładki wtedy nie ruszono.**

⭐ **Pomiar kontekstu przesądził o kierunku**: pozostałe **pięć** zakładek narzędziowych (Trace, Session,
Global Search, Script Executor, Data Import) niesie `IconResourceKey = "AccentBrush"`. Security Manager
był jedyną zakładką narzędziową na kolorze rodzaju — **5:1**, czyli nie „kwestia gustu", tylko odstępstwo.

To jest **#340 w czystej postaci**: decyzja żyje w JEDNYM miejscu (komentarz przy przycisku), rejestr
w DRUGIM, a zamykany bywa wyłącznie rejestr.

### §2.2 Jak wykonane

Jedna linia. 🔒 Decyzja użytkownika: **zmienia się wyłącznie barwa**; glif zostaje zależny od kontekstu
otwarcia (User/Role). Kolor odpowiada na pytanie *„czym jest ta zakładka"*, glif na *„na czym jest
otwarta"* — dwie osie, dwie odpowiedzi.

### §2.3 Strażnik

`ToolTabIdentityTests` — **pilnuje PRZESŁANKI, nie polityki** (#322): każda z sześciu fabryk zakładek
narzędziowych musi rozwiązywać kolor ikony do roli akcentu, czytane z **produkcyjnego źródła**, nie
z przepisanej tablicy (#333).

⭐ Druga asercja jest tym, co czyni pierwszą sensowną: **zakładka edytora obiektu NADAL musi nieść kolor
rodzaju**. Bez niej regułę dałoby się spełnić malując wszystko na akcent — czyli likwidując rozróżnienie,
dzięki któremu zakładka tabeli i zakładka procedury są rozpoznawalne na pierwszy rzut oka.

Zweryfikowany podsadzeniem; komunikat nazywa fabrykę.

---

## §3 Punkt 2 — `Button.primary`: tekst w stanach

### §3.1 Przyczyna, zmierzona headless w obu motywach i czterech stanach

Wariant `.primary` nadpisuje `Foreground` **na przycisku** (dziedziczenie) oraz na jawnych dzieciach
`TextBlock` i `SvgIcon` — ale **nigdy na `ContentPresenter` w szablonie**. Fluent ustawia
`ButtonForegroundPointerOver` / `…Pressed` / `…Disabled` **właśnie na presenterze**, a `FluentBridge`
mapuje dwa pierwsze na `ForegroundColor`.

⇒ Przycisk, którego `Content` jest **zwykłym stringiem** (tekst rysuje sam presenter), traci jasny tekst.
Przycisk z jawnym `<TextBlock>` — nie.

| motyw / kształt treści | HOVER tekst | tło | kontrast |
|---|---|---|---|
| **Light, string** | **`#1B1D1F`** | `#1A4F8F` | **2,04:1** ⛔ |
| Light, TextBlock | `#FFFFFF` | `#1A4F8F` | 8,33:1 ✓ |
| Dark, string | `#D4D4D4` | `#1A4F8F` | 5,62:1 ✓ |

⭐⭐ **Mechanizm był zepsuty w OBU motywach — tylko Light przekraczał próg.** „W Dark wygląda dobrze" było
zbiegiem okoliczności (`ForegroundColor` jest tam prawie biały), nie poprawnością. To rozróżnienie
zmienia diagnozę z „defekt motywu Light" na „defekt wariantu", i tylko drugie da się naprawić raz.

⚠ Ten sam mechanizm dotyczył `:pressed` (identyczne liczby) **i `:disabled`**: w Light string-content
malował `#5F6570`, a TextBlock-content `#8A9199` — dwa wyłączone przyciski główne w jednej stopce
różniły się barwą tekstu.

**Zasięg:** 22 przyciski `.primary` deklarują `Content=` (zagrożone) wobec 18 z dziećmi (odporne).

### §3.2 Jak wykonane

Trzy settery `Foreground` na `ContentPresenter`, ograniczone do `.primary`.

⛔⛔ **Świadomie NIE w `FluentBridge`.** `ButtonForegroundPointerOver` obsługuje każdy zwykły przycisk,
gdzie ciemny tekst na jasnej chromie jest **poprawny** — podniesienie go zepsułoby wszystkie pozostałe.
To ta sama racja, którą `ControlStyles.axaml` zapisuje już przy `ButtonBackgroundDisabled`.

### §3.3 ⭐⭐ Strażnik, którego pierwsza wersja była błędna — i to jest lekcja tej iteracji

Pierwsza wersja obejmowała progiem 4,5:1 **wszystkie cztery stany** i **zaświeciła na czerwono na
poprawnym produkcie**: stan nieaktywny daje **2,43:1 (Light) / 4,02:1 (Dark)**, bo jest **świadomie
przygaszony** — ratyfikowana decyzja z 2026-08-03 („stan nieaktywny to przygaszone wypełnienie, nie
przezroczystość").

⛔ **„Naprawa" oznaczałaby cofnięcie tamtej decyzji, żeby zadowolić próg, który nigdy nie miał dotyczyć
wyłączonej kontrolki.**

⭐ To jest **#322 popełnione WEWNĄTRZ strażnika pisanego przeciwko temu błędowi**: reguła wypowiedziana
o KLASIE („każdy stan") była fałszywa o jednym jej członku. Zakres zawężono do stanów **akcyjnych**;
stan nieaktywny pilnuje osobna asercja **WIĄZANIA** (presenter musi czytać rolę `OnAccentDisabledBrush`),
bo tam pytaniem nie jest kontrast, tylko czy setter w ogóle dosięga.

⭐ Treść testu jest stringiem **celowo i to jest cała jego siła**: z jawnym `<TextBlock>` test
przechodziłby PRZED poprawką, bo tamta ścieżka nigdy nie była zepsuta.

Oba strażniki zweryfikowane podsadzeniem; drugi odtworzył liczby z rozpoznania co do bitu.

---

## §4 Punkt 3 — Live DDL na pięciu powierzchniach

### §4.1 Zmierzony stan wyjściowy

**12** powierzchni szło już przez `SqlEditorBehavior.AttachReadOnlyHighlighting`. Poza nimi stało
**pięć** bajtowo identycznych kopii jednego kształtu — read-only `TextBox`, monospace,
`Binding DdlPreview`: `NewTableTabView`, `CheckConstraintDialog`, `ConstraintFieldDialog`,
`ForeignKeyDialog`, `IndexDialog`.

🔒 Decyzja użytkownika: **cała piątka** (R7 — inaczej cztery ekrany zostają niezgodne z resztą aplikacji).

### §4.2 ⭐ Dlaczego nie powstało pięć kopii

`AttachReadOnlyHighlighting` wpina **tylko warstwę semantyczną**. Druga połowa — wybór definicji XSHD dla
motywu, `SelectionBrush`, ponowne uruchomienie obu na `ActualThemeVariantChanged` — była **przepisana
ręcznie do dwunastu widoków** jako identyczny ~15-linijkowy `ApplyEditorTheme`. Pięć kolejnych byłoby
kopiami 13–17, czyli momentem, w którym duplikacja przestaje być zbiegiem okoliczności i staje się
mechanizmem.

Złożone w **`SqlEditorBehavior.AttachDdlPreview(editor, host)`** — jedno wywołanie na powierzchnię:
semantyka + leksyka + motyw + push tekstu.

⭐ Subskrypcja motywu siedzi **na edytorze**, nie na widoku-hoście, więc wpięcie nie wymaga niczego od
wołającego i nie może przeżyć kontrolki, którą dekoruje. To też powód, dla którego nie jest to klasa
bazowa: podgląd występuje w zakładce, w oknie dialogowym i w panelu, które nie mają wspólnego typu hosta.

Nowy `IDdlPreviewSource` **tylko zapisuje pojęcie, które już istniało** — wszystkie pięć VM-ów miało tę
własność pod tą samą nazwą.

⛔ **Dwunastu istniejących powierzchni świadomie NIE migrowano** — działający kod poza zakresem
zgłoszenia; ujednolicenie ich jest oczywistym follow-upem, nie zmianą do przemycenia przy okazji.

### §4.3 ⚠⚠ Granica warstwy semantycznej — zapisana, nie ukryta

`AttachReadOnlyHighlighting` rozwiązuje metadane przez
`editor.FindAncestorOfType<Window>()?.DataContext as MainWindowViewModel`. W **oknie dialogowym** tym
DataContextem jest własny VM dialogu, więc model zostaje `null` i maluje **wyłącznie warstwa leksykalna**.

To realne ograniczenie, nie defekt do ścigania w tym punkcie: dialog pokazuje słowa kluczowe, typy,
literały i komentarze w barwach aplikacji, a akcenty obiektów, których nie umie rozwiązać, dotyczą
w większości obiektu, który jeszcze nie istnieje.

⇒ **New Table** (żyje w `MainWindow`) dostaje obie warstwy; **cztery dialogi** — leksykalną.

### §4.4 Metryki

Podgląd w New Table dostał jawną rolę **`Text.Code.Size` (13)** zamiast wartości dziedziczonej — to
konwencja podglądu DDL w zakładce (`DataImportTabView`, `DomainDetailTabView`). Cztery dialogi zostają na
`Text.Compact.Size` (11), czyli swoim dotychczasowym rozmiarze. ⚠ Rozjazd 13/11 jest **świadomy i opisany
rodziną** (zakładka vs gęsty dialog); ujednolicenie byłoby decyzją typograficzną, a blok typografii M4
jest zamknięty.

### §4.5 Strażniki

`DdlPreviewSurfaceTests` (źródłowy — pilnuje, że każdy z pięciu podglądów jest wspólną powierzchnią SQL
**i że jego code-behind naprawdę woła `AttachDdlPreview`**; sam XAML maluje NIC) +
`ReadOnlyPreview_TakesTheThemeSyntaxDefinition_AndFollowsAThemeChange` (behawioralny — czyta
`SyntaxHighlighting` **zrealizowanej** kontrolki i wymaga, żeby **podążała za zmianą motywu**).

⭐ Druga połowa tego drugiego testu jest ważniejsza od pierwszej: wersja ustawiająca paletę raz
w konstruktorze przechodzi połowę i zostawia podgląd w barwach poprzedniego motywu — usterkę widoczną
wyłącznie po przełączeniu. Zweryfikowane podsadzeniem.

---

## §5 ⭐⭐ Znalezisko z samego renderu QA

Pierwszy render pokazał kolumnę „PO" **bez koloru** — identycznie jak „PRZED".

Przyczyna: definicje XSHD rejestruje `App.RegisterFirebirdSyntax`, a sonda uruchamia `ProbeApp`, więc
`HighlightingManager.GetDefinition` zwracało `null`.

⭐ To **ta sama cicha awaria co brakujący słownik zasobów** (§19.23.7), tylko o warstwę dalej: **brak
REJESTRACJI nie zawodzi — po cichu zabiera kolor, a obrazek wygląda wiarygodnie i odpowiada na inne
pytanie, niż zadano.** Gdyby render zrobiono „na oko", wniosek brzmiałby *„kolorowanie nie działa"*.

⚠ Przy okazji skorygowany komentarz w `ProbeApp`, który twierdził, że *„`AvaloniaEdit` świadomie
pominięty — żaden render tej sondy nie zawiera edytora tekstu"*. Przestał być prawdziwy w chwili, gdy
moduł QA zaczął renderować podgląd DDL — **#284 w kształcie komentarza: uzasadnienie przeżyło swój
powód.**

---

## §6 Punkt 4 — Performance / Execution plan (advanced)

Zakres zamknięty i odebrany: **4a (layout) + 4b (kolorowanie nazw)**. ⛔ Warianty **W1/W2 zostały
zbudowane, wyrenderowane i ODRZUCONE** — §6.5 zapisuje je tak, żeby nikt ich nie projektował po raz drugi.

### §6.1 4a — odstęp treści sekcji

`ExpanderContentPadding` był **zerem**, przy `ExpanderHeaderPadding` = `10,0,0,0`. Czyli jedyny element
z oddechem był tym, który go nie potrzebował — nagłówek — a treść (etykieta „Raw plan", przycisk Copy,
drzewo, ramka planu) stała przy krawędziach sekcji.

`Pad.Group` (10,8) pasuje **rolą, nie tylko liczbą**: jej pozioma składowa to ta sama dziesiątka, którą ma
nagłówek, więc treść ustawia się w jednej linii pionowej z tytułem. Pionowy `Margin="0,6,0,4"` zdjęty —
dawał go ten sam brak paddingu, a zostawiony dokładałby się do 8 z roli.

⚠ To obniżyło sufity odstępów w strażniku (`Spacing` 14 → 13, `Margin` 16 → 15) i **strażnik tego
zażądał**: usunięcie wartości lokalnych czyni wpis nieaktualnym w drugą stronę („progress that was not
written down"). Ratchet działający zgodnie z zamysłem.

### §6.2 ⭐⭐ 4b — parser już istniał, a widok wyrzucał jego wynik

To jest główne znalezisko punktu 4 i wyszło z Fazy 0, nie z implementacji.

`PlanNode` niesie **`Method` / `TableName` / `Alias` / `IndexName` / `Detail`**, klasyfikowane przez
`PlanNodeDescriptor.Parse` dla **każdego** węzła. A widok renderował:

```csharp
PlanNodeViewModel.DisplayText => Node.RawText;   // cała klasyfikacja wyrzucona
```

⇒ Kolorowanie planu **nie potrzebowało nowej gramatyki** — potrzebowało, żeby widok przestał spłaszczać.

⛔ **Hipoteza „użyć syntax highlightingu SQL" była złym narzędziem, i to mierzalnie:** plan Firebirda nie
jest SQL-em. `Table`, `Bitmap`, `Access By ID`, `Range Scan` nie są słowami kluczowymi SQL, a XSHD
pokolorowałby słowa przypadkowe, mijając każdą nazwę, która ma znaczenie.

### §6.3 ⚠⚠ Podział bierze się z ROZCIĘCIA tekstu surowego, nigdy ze składania

To decyzja **poprawnościowa**, nie stylistyczna. Składanie (`"Table " + nazwa + " as " + alias`) postawiłoby
`PlanTextSegments` w roli odtwarzacza wypowiedzi silnika, a każdy rozjazd **po cichu pokazałby użytkownikowi
plan, którego serwer nie wydrukował**. Rozcinanie oryginału czyni *„tekst renderowany == tekst surowy"*
prawdą **z konstrukcji** — ta sama dyscyplina §0, którą formater stosuje do źródła.

⭐ Granicę podziału wyprowadzono z faktu, że **`Detail` jest SUFIKSEM `RawText`** (parser produkuje go
przez wycięcie reszty i `Trim`). Dzięki temu **nie ma tu drugiej kopii listy słów kluczowych parsera** —
a gdyby ten niezmiennik przestał obowiązywać, `EndsWith` go wyłapuje i węzeł renderuje się w całości,
zamiast zostać przecięty w złym miejscu.

Pilnuje tego teoria nad **16 realnymi liniami × 6 klasyfikacji**; podsadzenie (zgubienie cudzysłowów)
zapala ją na każdej linii z nazwą.

### §6.4 ⚠⚠ Zmiana ROLI tokenu zmienia obowiązujący PRÓG — i to wymusiło korektę dwóch barw

`IconColor_*` to role rodzaju zaprojektowane dla **IKON**, a ikona podlega progowi **3:1** (element
nietekstowy). Plan maluje nimi **TEKST 11 px**, czyli ta sama wartość zmieniła rolę — a wraz z rolą próg na
**4,5:1** (§10 + §10.1, wymóg własny EmberTerna, jawnie nie „WCAG AA Large").

Zmierzone na `BackgroundColor`:

| token | Dark | Light |
|---|---|---|
| `IconColor_Table` | 8,27:1 ✓ | 4,68:1 ✓ |
| `IconColor_Index` | 10,03:1 ✓ | **4,00:1** ⛔ |
| `IconColor_Procedure` | ~8:1 ✓ | **3,70:1** ⛔ |

🔒 Ratyfikowany **wariant A**: przeliczyć **przy progu, zachowując odcień** (HSL — H i S bez zmian, opada
wyłącznie L). `IconColor_Index` `#558B2F` → **`#4F812C`** (4,54:1), `IconColor_Procedure` `#E65100` →
**`#CE4800`** (4,50:1). Metoda ta sama, co ratyfikowana w M5 §10.

⭐ **Jedna barwa na rodzaj w całej aplikacji** — to korekta TYCH tokenów, nigdy osobna paleta dla planu.
Zmiana dotyczy więc też ikon w Metadata Explorerze i na pasku: jako ikony trzymają **4,23:1 / 4,19:1**
wobec wymaganych 3:1, więc nic nie tracą. ⚠ Dark nietknięty — próg był tam spełniony z zapasem.

⛔⛔ **Korekta NIE jest osobną decyzją estetyczną, tylko warunkiem poprawności kolorowania.** Cofnięcie
jej przy zachowanym kolorowaniu wraca pod próg i zapala `EveryColourThePlanTreePaintsTextWith_…`.
⚠ Zapisane, bo w trakcie sprzątania po W1/W2 padła instrukcja „wycofaj wariant A" — słuszna pod
założeniem, że całe kolorowanie znika, i myląca, gdy kolorowanie zostaje.

### §6.5 ⛔⛔ W1/W2 — ZBUDOWANE, WYRENDEROWANE, ODRZUCONE. Nie projektować ponownie.

Po odbiorze 4b padło pytanie, czy nie pokolorować także **reszty** wiersza. Zbudowano cztery warianty
i wyrenderowano w obu motywach:

| wariant | struktura | czasownik Table/Index | nazwa | metoda dostępu |
|---|---|---|---|---|
| **W0 = wdrożone** | tekst | tekst | rodzaj | wycofana |
| W1 | wycofana | wycofany | rodzaj | tekst |
| W2 | tekst | wycofany | rodzaj | tekst |
| W3 | **nowy token** | wycofany | rodzaj | tekst |

**Powód odrzucenia, zmierzony:** katalog ma **dokładnie DWA** neutralne poziomy tekstu
(`ForegroundBrush`, `SubtleForegroundBrush`) — trzeciego nie ma. `IconColor_Query` jest optycznie
neutralny, ale to kolor **rodzaju**; użycie go dla „metody dostępu" nazwałoby rolę, której ten token nie ma.

⭐⭐ **A te dwa poziomy dzieli w Dark zaledwie 1,78:1** (Light: 2,88:1). Czyli „pełna siła" i „wycofane"
to w Dark dwie bliskie szarości — przy 11 px monospace czytają się jak ta sama barwa. **W2 zostało
wdrożone, obejrzane i wycofane właśnie dlatego**, że deklarowane rozróżnienie nie było wiarygodnie widoczne.

⚠⚠ **Droga do tego wniosku jest warta więcej niż wniosek**: QA zgłosiło, że „metoda dostępu nadal jest
szara", co brzmiało jak defekt implementacji. Pomiar rozstrzygnął inaczej — segment **miał** właściwy klucz
(`[Access By ID] -> ForegroundBrush`), klucz **rozwiązywał się** poprawnie, a piksele renderu pokazały
`#D4D4D4` w Dark i `#1B1D1F` w Light, czyli **dokładnie pełną siłę**. Kod był poprawny; niewidoczna była
RÓŻNICA. ⭐ Bez pomiaru pikseli „naprawiałbym" działający mechanizm.

⛔ **Nie wracać do tego bez NOWEGO pomiaru.** W3 (trzeci poziom) jest odrzucony podwójnie: wymaga nowej
wartości i wydaje ją na to, żeby uczynić strukturę *pół-cichą*, gdy pytanie projektowe jest binarne.

### §6.6 Co zostało nietknięte

- **Raw plan** — wierny monospace bez kolorowania; to escape hatch dla węzła, którego parser nie rozpoznał
- **Copy** — bez zmian. ⚠ Zmierzone i warte zapisania: przycisk obok etykiety „Raw plan" **nie kopiuje
  samego planu**, tylko ładunek „expert drawer" (timings + capture + plan pod nagłówkiem), zgodnie z własnym
  docstringiem. Zachowanie zastane, świadomie nieruszone; test pinuje, że surowy plan przechodzi przez nie
  **bez zmian**
- **`PlanParser` / `PlanNode` / `PlanNodeDescriptor`** — ani jednej linii; punkt 4 wyłącznie *konsumuje* to,
  co już produkowały

### §6.7 Strażniki

`PlanTextSegmentTests` (10, czyste — bez sesji headless, bo `PlanTextSegments` zwraca KLUCZE, nie pędzle)
+ dwa w `DesignTokenApplicationTests`: próg kontrastu na barwach, którymi plan maluje tekst, oraz
rozwiązywalność każdego klucza **przez `IconBrushConverter`, którego używa widok**.

⭐ Ten drugi nie jest zbędny wobec pierwszego: konwerter czyta wyłącznie `Application.Resources`, czyli
**węższą ścieżkę niż `FindResource`**. Klucz spoza tego słownika zwróciłby `UnsetValue`, a `UnsetValue` na
`Foreground` **nie zawodzi** — element po cichu dziedziczy barwę rodzica i wygląda jak „segment się nie
pokolorował". Sonda renderująca używa szerszej ścieżki, więc mogłaby pokazać poprawny obraz dla wiązania,
które w aplikacji by nie zadziałało.

---

## §6a ⚠⚠ Lekcja procesowa z tego punktu — sprzątanie skasowało zaakceptowaną pracę

Po odrzuceniu W1/W2 padła instrukcja sprzątnięcia eksperymentu. Zinterpretowałem ją **za szeroko**
i usunąłem **cały 4b** — mechanizm, który był już odebrany — zamiast wyłącznie nadbudowy W1/W2.

⛔⛔ **Odzysk z gita był niemożliwy i to jest sedno:** te pliki **nigdy nie były commitowane**
(`git log --all` → pusto, brak dangling blobów, DLL sondy przebudowany). **Plik nieśledzony i usunięty
nie ma w gicie żadnej siatki bezpieczeństwa** — `git checkout` przywraca do HEAD, czyli do stanu *sprzed
całego punktu*. Stan odtworzono z dosłownego zapisu sesji.

⭐ Dwa wnioski na przyszłość:
1. **Commituj odebrany etap, zanim zaczniesz na nim eksperymentować.** 4b było zaakceptowane i przez cały
   eksperyment pozostawało nieskommitowane — dlatego „wycofaj eksperyment" nie miało do czego wrócić.
2. **Instrukcja sprzątająca wydana pod błędną przesłanką jest błędna w tym samym stopniu.** Polecenie
   „wycofaj wariant A" było słuszne, gdy znikało całe kolorowanie, i szkodliwe, gdy zostawało — a to ja
   miałem to zauważyć, bo to ja znałem zależność między wariantem A a progiem §10.

---

## §7 Weryfikacja (pakiet 1–3)

- Build **0/0**
- Suite **8401** = 8218 (główna) + 128 (headless zgrupowana) + 55 (headless izolowana), wszystkie
  `--blame-hang`, wszystkie zielone
- ⭐ **Krucha ręczna lista nazw w filtrze partycji headless nie urosła** — oba nowe testy headless
  dołączyły do istniejącej klasy `DesignTokenApplicationTests`
- Smoke czysty
- Render QA obu motywów: `tools/probes/VisualCandidateProbe -- qa123`
- **Wszystkie pięć nowych strażników zweryfikowane podsadzeniem**
- QA wizualne użytkownika: **przyjęte bez uwag**

---

## §8 Weryfikacja (punkt 4)

- Build **0/0**, zero ostrzeżeń
- Suite **8429** = 8242 (główna) + 132 (headless zgrupowana) + 55 (headless izolowana), wszystkie
  `--blame-hang`, wszystkie zielone
- ⭐ Krucha ręczna lista nazw w filtrze partycji headless **nie urosła** — oba nowe strażniki dołączyły do
  istniejącej klasy `DesignTokenApplicationTests`, a `PlanTextSegmentTests` są CZYSTE (bez Avalonii)
- Smoke czysty
- Render: `dotnet run --project tools/probes/VisualCandidateProbe -- plan` (oba motywy)
- Strażniki zweryfikowane podsadzeniem: niezmiennik tekstu (zgubienie cudzysłowów), próg kontrastu
  (przywrócenie `#558B2F`), ścieżka konwertera (usunięcie klucza z `Application.Resources`)
- QA wizualne użytkownika: **przyjęte**

⚠ **`Lab/EmberTern_Lab.fdb` bywa modyfikowany przez SMOKE TESTY** — aplikacja podłącza się do laba,
a Firebird dotyka nagłówka pliku. To zacommitowany artefakt binarny, który zmienia się „sam"; sprawdzaj
`git status Lab/` przed każdym commitem po smoke. Nie ma to związku z żadną zmianą w kodzie.

---

## §9 Punkt 5 — Settings UX

Zgłoszenie: okno ustawień jest funkcjonalne, ale wygląda surowo — mała lista kategorii bez ikon, brak
separacji lewej i prawej strony, wszystko stłoczone, całość czyta się jak techniczny formularz.

### §9.1 ⭐⭐ Rozpoznanie: cztery przyczyny, a największa jest defektem HOSTA, nie stylu

**(1) Przesłanka stylu karty nie obowiązywała w tym jednym oknie.** `Border.settings-group` maluje się
`BackgroundBrush` i opisuje siebie jako *„a recessed BackgroundBrush surface inside the **PanelBrush** chrome
that hosts it"*. Zmierzone u wszystkich czterech konsumentów:

| plik | kontener karty | działa? |
|---|---|---|
| `SettingsExportDialog.axaml:29` | `PanelBrush` | ✓ |
| `SettingsImportDialog.axaml:38` | `PanelBrush` | ✓ |
| `DataImportTabView.axaml:61` | `PanelBrush` | ✓ |
| **`SettingsWindow.axaml`** | **żaden** | ⛔ |

Karta stała wprost na tle okna: `#1E1E1E` na `#1E1E1E` (Dark), `#FCFCFD` na `#FCFCFD` (Light) — **różnica
zerowa**, całą separację 17 kart niosła kreska 1 px. ⛔ Naprawiony jest HOST; styl **nietknięty**, bo u tamtych
trzech działa poprawnie. Gotcha **#351**.

**(2) Panel nawigacji nie był powierzchnią** (`Background="Transparent"` na oknie `BackgroundBrush`).
**(3) Wiersz kategorii stał na roli opisującej inny gest** — `Size.Row.Menu` (22) to *„czytasz w pionowej serii
i wybierasz jednym kliknięciem"*, a nawigacja Settings to sześć pozycji, na których się LĄDUJE.
**(4) Wewnątrz strony brak grupowania** — Editor to był płaski ciąg sześciu równorzędnych kart.

### §9.2 Ratyfikowane decyzje

🔒 **T‑1** — trzy tony, zero nowych tokenów: nawigacja `ChromeStrongBrush` › treść `PanelBrush` › karta
`BackgroundBrush`. Monotonicznie w obu motywach; każdy krok to adjacencja, której aplikacja już używa
(pasek tytułu › panel › edytor).
⛔ Wariant „karta = `SurfaceRaisedBrush`" **odrzucony POMIAREM**: w Light dałby `#FFFFFF` na `#FCFCFD` = trzy
jednostki, czyli dokładnie defekt opisany przy `TextBox` na powierzchni unoszącej się (#308).

🔒 **Ikony** — sześć, wszystkie ISTNIEJĄCE, żadnej nowej geometrii: `Icon.Settings` · `Icon.Braces` ·
`Icon.Table` · `Icon.PanelLeft` · `Icon.Crosshair` · `Icon.PencilRuler`. ⛔ Debugger bierze `Icon.Crosshair`,
a nie kompozytu `DebuggerIcon` z paska — kompozyt to osobna kontrolka o dwóch barwach i własnym
`ControlTheme`, więc byłby wyjątkiem w szablonie wiersza i drugim mechanizmem rysowania ikony kategorii.

🔒 **`Size.Row.Tree` (24)** — bez tworzenia roli `Size.Row.Nav`.
🔒 **Bez ikon przy 17 parametrach** i **bez podnoszenia wysokości `TextBox`/`ComboBox`** — pierwsze nie
przechodzi bramki `color-language.md` §0.5 („nie wiem" = odmowa), drugie to rola app-wide i backlogowany
sprint gęstości (R7 + R18).

### §9.3 ⭐ Wskaźnik aktywnej kategorii — zero zmiany geometrii Z KONSTRUKCJI

Pasek `Size.TabIndicator` (2) stoi w szablonie wiersza **zawsze**; zaznaczenie zmienia wyłącznie jego BARWĘ.
⛔ Odrzucony wariant `BorderThickness` na `ContentPresenter` wiersza: ramka wchodzi w desired size, a presenter
Fluenta jest o nią wcinany, więc pasek dokładany przy zaznaczeniu rozpychałby wiersz przy **każdym kliknięciu**
— §13.3 Zero Layout Shift złamane dokładnie tam, gdzie zmiana działa (pomiar z M5 / L‑1). To ten sam wzorzec,
który L‑1 ratyfikowało dla `Button.primary`.
⛔ Świadomie **bez pogrubienia** etykiety w stanie aktywnym: `SemiBold` zmienia szerokość tekstu, czyli wnosi
ruch, którego pasek został tak skonstruowany, żeby uniknąć.

### §9.4 ⭐⭐ B4 — cztery pozycje Easy-mode w jednej karcie, wyłącznie prezentacja

Cztery flagi zostają **czterema wierszami katalogu**: własne id, haystack, wartość i `IsVisible`. Zmienia się
tylko to, że widok rysuje wokół nich JEDNĄ ramkę — bo to jeden temat („w jakim trybie otwiera się edytor
obiektu"), a cztery równorzędne karty mówiły cztery tematy. ⛔ Zero zmian w `SettingsCatalog`, zero nowych
abstrakcji; jedyny dodatek to `ShowEasyModeGroup` (OR czterech `IsVisible`) w **dokładnym kształcie
istniejącego `ShowTabStripMaxRows`**.
⚠ Każdy checkbox zachowuje SWOJE `IsVisible`, więc wyszukanie „procedure" pokazuje kartę z jednym wierszem —
znaczenie filtra nietknięte, zmienił się wyłącznie pojemnik.
⭐ Skutek uboczny: strona Editor **zmieściła się w całości** (6 kart → 3); wcześniej przewijała się i ucinała
ostatni opis w połowie zdania.

### §9.5 ⭐⭐ Dwie rzeczy złapał POMIAR PIKSELI, nie oko

**(a) Wskaźnik w ogóle nie działał, a render wyglądał wiarygodnie.** Zmierzone x=12..24 → `#094771`
(wypełnienie zaznaczenia) zamiast `#2D6BBF`. Przyczyna to **#342**: `Background="Transparent"` zadeklarowane
lokalnie w szablonie **bije każdy setter stylu**, więc `:selected` nie miał czego pomalować. ⚠ Brak 2 px paska
na obrazku wygląda po prostu jak brak paska — bez pomiaru odebrałbym to jako „działa".

**(b) Zgłosiłem defekt, którego nie ma — i wycofałem go przed zmianą kodu.** Pierwszy pomiar dał „panel treści
= `#1E1E1E`, czyli `PanelBrush` nie dochodzi". Przekrój pionowy pokazał, że punkt trafił w **stopkę**. Panel
treści jest `#252526`. ⚠ Zły punkt pomiarowy, nie zły kod — i to jest ta sama lekcja co §6.5: pomiar rozstrzyga
także wtedy, gdy rozstrzyga na moją niekorzyść.

### §9.6 ⚠⚠ Trzy istniejące strażniki padły na POPRAWNYM produkcie

**(a) Dwa testy przepisywały przesłankę „jeden wiersz katalogu = jedna karta"** (`17 vs 14`, `6 vs 3`) — **#333**.
Zgrupowanie Easy-mode tę przesłankę łamie z założenia. ⛔ Asercji nie osłabiono i grupowania nie cofnięto:
przeformułowano ją na to, o co jej własny komentarz mówi, że chodzi (*„złapać wiersz dodany do katalogu bez
bloku XAML"*) — każdy wiersz musi mieć **swoją etykietę na ekranie** (tożsamość, nie licznik) **plus** kart nie
może być więcej niż wierszy (brak karty-sieroty). Razem: ani wiersz bez UI, ani karta bez wiersza, i żadna
z tych asercji nie zakłada, ILE kart jest. Zweryfikowane podsadzeniem (usunięcie jednego checkboxa →
`wiersz katalogu 'editor.functionEasyMode' nie ma etykiety na ekranie`).

**(b) `TheCatalogTableContainsNoStringLiterals` padł i MIAŁ RACJĘ.** Klucze ikon to ciągi w tablicy katalogu.
⛔ Strażnika nie ruszono — klucze dostały nazwy jako `const` obok istniejących `CategoryGeneral`/`SettingTheme`,
czyli tam, gdzie ten plik już trzyma swoje wartości.
⭐ Do tego nowy strażnik **`EveryCategoryIcon_ResolvesToARealGeometry`**, bo ryzyko jest realne:
`IconGeometryConverter` na nieznanym kluczu zwraca `null`, więc literówka **usuwa ikonę po cichu przy zielonym
buildzie** — #348 w kształcie ikony. Czyta ŹRÓDŁO `IconGeometries.axaml`, nie przepisaną listę (#333);
zweryfikowany podsadzeniem literówki.

**(c) Ratchet odstępów zażądał korekty w drugą stronę** — `Margin` w `SettingsWindow.axaml` 11 → 8 (dwie
wartości zeszły na rolę `Margin.FieldGap`, jedna zniknęła z marginesem siatki zastąpionym paddingami paneli),
suma 474 → 471. To samo zachowanie co przy 4a (§6.1): „progress that was not written down".

### §9.7 ⚠ Granice zapisane świadomie

- **`Pad.Dialog` (20,16) dostał pierwszego konsumenta** jako padding panelu treści. ⚠ CLAUDE.md notuje tę rolę
  jako „zero konsumentów" przy **odłożonym pytaniu o padding NAGŁÓWKÓW dialogów** — to inne miejsce, ale
  zapisane, żeby tamta decyzja zastała stan zgodny z opisem.
- **Kreska pionowa zostaje** mimo różnicy tonalnej: krok Chrome→Panel to 8 jednostek w Dark i 11 w Light, czyli
  separacja czytelna, ale miękka. Szerokość z roli `Stroke.Hairline` — katalog **nie ma** roli `Thickness` dla
  krawędzi prawej, a wymyślanie jej dla jednego elementu byłoby otwarciem etapu odstępów tylnymi drzwiami.
- **Stopka zostaje na tle okna**, jako osobne pasmo pod obiema powierzchniami.
- ⛔ **Dwunastu innych powierzchni nie ruszano** — zakres to jedno okno.

### §9.8 Sonda

`tools/probes/VisualCandidateProbe/SettingsUx.cs` (`-- settings before|after`) renderuje **prawdziwe okno
`SettingsWindow`** z prawdziwym `PreferencesService`, wszystkie sześć kategorii × oba motywy.
⭐ To odwrotność pozostałych modułów tej sondy: tam kandydat żyje w sondzie i nic się nie wdraża przez samo
uruchomienie, tu sonda pokazuje stan WDROŻONY — więc kolumnę „przed" trzeba było wyrenderować **przed** zmianą
kodu i zachować jako pliki. Obie kolumny pochodzą z tego samego kodu sondy, więc różnica może pochodzić
wyłącznie z produktu. Powód jest wprost lekcją **#348**: obrazek zbudowany z atrap wygląda wiarygodnie
i odpowiada na inne pytanie, niż zadano.

### §9.9 Weryfikacja

- Build **0/0**, zero ostrzeżeń
- Suite **8430** = 8243 (główna) + 132 (headless zgrupowana) + 55 (headless izolowana), wszystkie
  `--blame-hang`, wszystkie zielone
- ⭐ Krucha ręczna lista nazw w filtrze partycji headless **nie urosła** — nowy strażnik ikon czyta ŹRÓDŁO,
  więc trafił do partycji głównej
- Smoke czysty; `git status Lab/` pusty (sesja nie łączyła się z labem)
- Render: `dotnet run --project tools/probes/VisualCandidateProbe -- settings after`, 12 plików
- Wszystkie trzy zmienione/nowe strażniki **zweryfikowane podsadzeniem**
- QA wizualne użytkownika (oba motywy): **przyjęte bez uwag**

---

## §10 Punkt 6 — Database Properties · KROK 0 (sonda pomiarowa)

⛔ **Krok 0 nie dostarczył ani jednej linii kodu produkcyjnego** — żadnego dialogu, VM, pozycji menu ani
writera. Jego produktem jest POMIAR, a narzędziem `tools/probes/DatabasePropertiesProbe` (sonda
diagnostyczna, świadomie poza solucją; opis i uzasadnienie: `tools/probes/README.md`).

Środowisko: **Firebird 5.0.3** (`WI-V5.0.3.1683`), **ODS 13.1**, sterownik **10.3.4**, baza scratch
WIN1250 / dialect 3 pod ścieżką ASCII. ⚠ `Lab/EmberTern_Lab.fdb` **nietknięty** — sonda z definicji zmienia
nagłówek bazy, a lab jest zacommitowanym artefaktem binarnym.

### §10.1 Źródła odczytu

`MON$DATABASE` ma na FB5 **28 kolumn** — wszystkie z wcześniejszego rozpoznania potwierdzone, plus
`MON$GUID`, `MON$FILE_ID`, `MON$SEC_DATABASE`, `MON$CRYPT_STATE`, `MON$BACKUP_STATE`, `MON$REPLICA_MODE`,
`MON$NEXT_ATTACHMENT`, `MON$NEXT_STATEMENT`. `RDB$DATABASE` ma **6**, w tym nieprzewidziane
`RDB$SQL_SECURITY`.

⚠ Dwie pułapki prezentacyjne, obie zmierzone, nie przewidziane:
- **`RDB$LINGER` na bazie, która go nie ustawiała, czyta się jako NULL, nie `0`** — „nie ustawione" i „0 s"
  to dwa różne stany.
- **`MON$OWNER` wraca dopełniony spacjami** (CHAR 252) — wymaga `TRIM`.

⚠ `MON$DATABASE_NAME` zwraca ścieżkę **wielkimi literami** (`C:\TEMP\…`), czyli formę silnika, a nie tę
z profilu połączenia.

**⭐⭐ `ENGINE_VERSION` NIE jest zamiennikiem `ServerVersion` — i to wycofuje moją własną rekomendację
z rozpoznania.** W reconie zalecałem „reuse before create: wersję silnika już mamy w
`FbConnection.ServerVersion`, nie dodawajmy zapytania". Zmierzone:

| źródło | wartość |
|---|---|
| `RDB$GET_CONTEXT('SYSTEM','ENGINE_VERSION')` | `5.0.3` |
| `FbConnection.ServerVersion` | `WI-V5.0.3.1683 Firebird 5.0/tcp (STREAMSOFT-0089)/P16:C` |

To nie są te same dane: banner sterownika niesie **nazwę maszyny serwera** i protokół. ⇒ zapytanie
kontekstowe **nie jest redundantne**, a „reuse before create" zastosowane bez pomiaru pokazałoby
użytkownikowi nazwę hosta w polu „wersja silnika".

### §10.2 Kontrakt `FbConfiguration` — symbol to nie zachowanie

Rozpoznanie potwierdziło jedynie, że nazwy sześciu metod **istnieją w binarce 10.3.4**. To dowód
o SYMBOLU, nigdy o działaniu ani o sygnaturze — ten sam kształt co **#321** (*brak błędu restore jest
dowodem o metadanych, nigdy o zgodności*). Refleksja w działającym procesie:

```
FirebirdSql.Data.Services.FbConfiguration : FbService
ctor(String connectionString)                     ← jedyny konstruktor, brak właściwości Database

Task SetAccessModeAsync   (Boolean readOnly,      CancellationToken)
Task SetForcedWritesAsync (Boolean forcedWrites,  CancellationToken)
Task SetPageBuffersAsync  (Int32   pageBuffers,   CancellationToken)
Task SetReserveSpaceAsync (Boolean reserveSpace,  CancellationToken)
Task SetSqlDialectAsync   (Int32   sqlDialect,    CancellationToken)
Task SetSweepIntervalAsync(Int32   sweepInterval, CancellationToken)
```

⚠ **`SetAccessMode` bierze `bool`, nie enum** — projekt zakładał `FbAccessMode`; obalił to kompilator.
⚠ Refleksja statyczna (poza procesem) **nie dała odpowiedzi w ogóle**: z assembly rozwiązało się 7 typów,
bo graf zależności wymaga prawdziwego hosta. Pomiar musiał być URUCHOMIONY, nie odczytany.

### §10.3 Zapis — wyłączność i moment zadziałania

| Właściwość | Zapis | Wymaga wyłączności | Kiedy efekt |
|---|---|---|---|
| Sweep interval | ✅ | nie | natychmiast |
| Forced writes | ✅ | nie | natychmiast |
| Reserve space | ✅ | nie | natychmiast |
| SQL dialect | ✅ | nie | natychmiast |
| **Page buffers** | ✅ (bez wyjątku) | nie | ⛔ dopiero po **pełnym zwolnieniu** bazy |
| **Read only** | ⛔ odrzucony | **TAK** | — |

**Read Only** odrzucony przy jednym otwartym attachmencie: SQLSTATE `40001`, GDS `335544510` (lock timeout)
+ `335544453` (object in use) — *„lock time-out on wait transaction / object … is in use"*. Po zamknięciu
wszystkich attachmentów ta sama operacja **przeszła**. ⇒ wymaganie wyłączności jest zmierzone, nie założone.
⚠ EmberTern trzyma **2–3 attachmenty na profil**, więc z poziomu połączonej aplikacji ta operacja nie ma jak
się powieść.

**SQL dialect zadziałał ONLINE** (3 → 1 → 3, przy otwartym attachmencie) i został przywrócony. ⇒ jego
pozostawienie do odczytu jest decyzją PRODUKTOWĄ (zmiana dialektu wpływa na SQL, którego używa sam
EmberTern), a nie ograniczeniem technicznym — i tak trzeba to zapisywać, żeby nikt nie „naprawił" tego
później jako rzekomego braku.

### §10.4 ⭐⭐ Page buffers — odczyt i zapis NIE dotyczą tej samej rzeczy

Zwykła sekwencja odczyt → zapis → odczyt **nie rozstrzyga** tego przypadku, więc dostał własny scenariusz
z trzymanym attachmentem („keeper"):

| krok | wynik |
|---|---|
| świeża baza, odczyt | **51200** (domyślna serwera) |
| zapis `1024` przy otwartym attachmencie | OK |
| odczyt na **nowym attachmencie**, baza wciąż w użyciu | **51200** — bez zmiany |
| po **pełnym zwolnieniu** bazy | **1024** |

⇒ **`MON$PAGE_BUFFERS` raportuje CACHE DZIAŁAJĄCEJ INSTANCJI, a nie zapisany nagłówek**, a zmiana obowiązuje
przy następnym **pełnym otwarciu bazy** — nie przy następnym attachmencie. Rozróżnienie „nowy attachment" vs
„pełne zwolnienie" było niewidoczne bez izolacji.

⚠⚠ **Konsekwencja, którą ujawnił dopiero osobny pomiar:** pole edycji zasiane z `MON$PAGE_BUFFERS`
pokazałoby **51200 — wartość DZIEDZICZONĄ z serwera** — a zapis bez żadnej edycji przypiąłby ją do tej bazy
na stałe. Samo otwarcie okna i kliknięcie Apply zamieniłoby „dziedzicz" w „przypięte", cicho.
⭐ Zmierzone osobno: **`SetPageBuffersAsync(0)` jest zapisywalne** i po zwolnieniu przywraca 51200, więc 0
znaczy „dziedzicz" i operacja **jest odwracalna** — ale „dziedziczone" i „przypięte 51200" są przez `MON$`
**nierozróżnialne**.

### §10.5 Services API i uprawnienia

| Przypadek | Wynik |
|---|---|
| poprawne hasło | OK |
| **puste hasło** (profil bez zapisanego hasła) | `No user password was specified.` — błąd sterownika, bez SQLSTATE/GDS |
| **błędne hasło** | ⚠⚠ `Not supported plugin 'Legacy_Auth'.` |
| bez `Database` w connection stringu | `Action should be executed against a specific database.` |
| zapis jako użytkownik bez uprawnień | SQLSTATE `28000`, GDS `335544788/335545112`: **`System privilege USE_GFIX_UTILITY is missing`** |
| odczyt `MON$DATABASE` jako użytkownik bez uprawnień | **działa** |

⚠⚠ **Błędne hasło NIE mówi „błędne hasło".** Sterownik jest Srp-only, więc po odrzuceniu poświadczeń serwer
schodzi do Legacy_Auth i użytkownik dostaje komunikat o **pluginie**. To rozszerza istniejący zapis
w CLAUDE.md (*„connection errors show the raw server message"*) na Services API i jest dokładnie tym
rodzajem komunikatu, który wygląda na defekt konfiguracji, a opisuje literówkę w haśle.

⛔ **`Database` jest w connection stringu WYMAGANE**, a `FirebirdTraceService.BuildServiceConnectionString`
buduje string **bez bazy** (Services „no-database"). ⇒ nie da się go użyć wprost; potrzebny jest wariant
z bazą, co jest faktem o kształcie API, nie o naszym kodzie.

⭐ Bramka uprawnień daje **konkretny, cytowalny komunikat**, więc własny pre-check jest zbędny — zgodnie
z dyrektywą użytkownika, żeby go nie budować.

### §10.6 Czego krok 0 świadomie NIE rozstrzygnął

⛔ Zakresu pól edytowalnych, kształtu dialogu, wiązania z menu ani sposobu zapisu — na wyraźne polecenie
użytkownika propozycja powstaje **po** pomiarze i osobno.
⚠ Niezmierzone i zapisane jako takie: zachowanie przy bazie na **serwerze zdalnym** (mierzone na
`localhost`), zachowanie przy **Firebirdzie 3/4** (sonda uruchomiona wyłącznie na FB5) oraz `RDB$LINGER`
jako wartość zapisywalna (`ALTER DATABASE SET LINGER` — nie było przedmiotem kroku 0).

---

## §11 Punkt 6 — Database Properties (implementacja + domknięcie)

Krok 0 (§10) dostarczył pomiar; ten rozdział opisuje to, co z niego zbudowano. Zakres ratyfikowany przez
użytkownika **po** obejrzeniu tabeli pomiarowej — żadna pozycja nie została wybrana z analizy.

### §11.1 Ratyfikowany zakres V1

**Edytowalne** — wyłącznie te, które pomiar wykazał jako zapisywalne ONLINE, bez wyłączności, widoczne
natychmiast: **Sweep interval · Forced writes · Reserve space**.

**Informacyjne:** Database · Owner · Engine version · ODS · Dialect · Charset · Created · Page size ·
Pages · Size · Page buffers · Linger.

⛔ **Poza V1, każde z powodem:**
- **SQL dialect** — zmierzony jako zapisywalny ONLINE (3 → 1 → 3 przy otwartym attachmencie), więc jego
  „tylko odczyt" jest **decyzją produktową, nie ograniczeniem**: zmiana dialektu wpływa na SQL, którego
  używa sam EmberTern.
- **Page buffers** — patrz §11.3.
- **Read Only** — usunięty **w całości** przy domykaniu etapu, patrz §11.6.
- `MON$GUID`, `MON$FILE_ID`, `MON$SEC_DATABASE`, `MON$CRYPT_STATE`, `MON$BACKUP_STATE`, `MON$REPLICA_MODE`,
  `MON$NEXT_*`, `RDB$SQL_SECURITY` — sonda potwierdziła, że istnieją; ⛔ nie dodajemy pola dlatego, że
  katalog je udostępnia.

### §11.2 ⭐⭐ Jedna obietnica, na której stoi bezpieczeństwo funkcji

**Otwarcie okna i naciśnięcie Apply musi nie wysłać NICZEGO.** To okno pisze do współdzielonej bazy
produkcyjnej, przez API bez możliwości wycofania, poza wszystkimi trzema lane'ami i poza transakcją
użytkownika — więc Apply wysyłający pełny snapshot przepchnąłby wartości, na które użytkownik nawet nie
spojrzał.

Realizacja: `DatabaseConfigurationChange` ma wszystkie składowe **nullowalne**, a `null` znaczy *nie ruszaj*.
Obietnica jest więc prawdziwa **z konstrukcji**, nie przez staranność. Pilnują jej trzy testy (Apply bez
edycji nic nie wysyła · edytowana wartość cofnięta do pierwotnej nie jest wysyłana · niepoprawna liczba nie
wysyła nic) oraz — mocniej — **weryfikacja na żywym FB5**: wysłano `sweep=4321 forced=<nic> reserve=False`,
a po zapisie `ForcedWrites` pozostało `True`.

⚠ **Apply NIE jest atomowy i to jest fakt o API, nie wybór projektowy** — każde ustawienie to osobne
wywołanie Services. Stąd LISTA wyników per ustawienie zamiast jednej flagi: po częściowym sukcesie
użytkownik musi wiedzieć, **które** zmiany są już w bazie. Komunikat wymienia to, co się udało, i dopiero
potem awarie.

### §11.3 ⭐⭐ Page buffers — odczyt i zapis nie dotyczą tej samej rzeczy

`MON$PAGE_BUFFERS` raportuje **cache działającej instancji**, a nie zapisany nagłówek (§10.4). W praktyce:
pole edycji zasiane tą wartością pokazałoby **51200 — liczbę DZIEDZICZONĄ z serwera** — a Apply bez żadnej
edycji **przypiąłby ją do tej bazy na stałe**.

🔒 Ratyfikowane: **informacja, bez pola edycji**, z notą mówiącą wprost, że to cache instancji, a zapisana
wartość zaczyna obowiązywać po pełnym zwolnieniu bazy. Bez tej noty liczba czytałaby się jak ustawienie.
⛔ Zaprojektowany i **odrzucony** przez użytkownika wariant „Current + Set to + Use server default": nie
wprowadzamy w V1 osobnego modelu *wartość bieżąca vs intencja vs dziedziczenie*, zwłaszcza gdy `MON$` nie
odróżnia „dziedziczone 51200" od „jawnie ustawione 51200".

### §11.4 Architektura

- **Odczyt** → lane **Metadata** (read-only, implicit per-command transaction), wzorzec
  `FirebirdSessionReader`. ⭐ **Bez bramkowania wersją** — cały zestaw kolumn zmierzono na **FB3.0.13
  (ODS 12.0)** i **FB5.0.3 (ODS 13.1)**; to pomiar, nie założenie.
- **Zapis** → **Services API**, własne połączenie poza lane'ami ⇒ zero ekspozycji na transakcję użytkownika.
  ⛔ `FirebirdTraceService.BuildServiceConnectionString` **nie nadaje się wprost** — buduje string *bez bazy*,
  a operacje konfiguracyjne wymagają `Database` (zmierzone).
- **VM bierze czytnik i writer jako DELEGATY**, nie jako typy Firebirda — to nie ceremonia warstw, tylko
  powód, dla którego każda reguła tej funkcji jest testowalna **bez serwera**, czyli dokładnie tam, gdzie
  leży ryzyko.
- **Menu**: `Properties…` w istniejącym `SidebarConnectionMenu`, ⚠ **wyszarzane, nie ukrywane** — inaczej
  niż sąsiedzi w tym samym menu. To ratyfikowana reguła z M3.4b część 2 (ukrywaj, gdy pozycja nie ma sensu
  w ogóle; wyszarzaj, gdy operacja istnieje, ale jest chwilowo niedostępna), a nie dryf. Zero nowej
  maszynerii: `CanDisconnect` już znaczy „wymaga połączenia".

### §11.5 Błędy

⭐ **Surowy komunikat serwera ZAWSZE**, a krótki lead **wyłącznie** dla przypadków rozpoznanych po
**SQLSTATE/GDS** — wzorzec `DebugErrorClassifier`.
⛔⛔ Zmierzony komunikat przy błędnym haśle (`Not supported plugin 'Legacy_Auth'`) **nie jest tu tłumaczony**:
przychodzi **bez SQLSTATE i bez GDS**, więc rozpoznać go dałoby się wyłącznie po tekście. Osobny strażnik
pilnuje, że **pozostaje nieklasyfikowany**. (Odrębna sprawa: sam komunikat POŁĄCZENIA został poprawiony —
§11.7.)
⚠ **Apply jest niedostępny z góry tylko w jednym przypadku** — profil bez zapisanego hasła, bo sterownik
odmawia zanim dojdzie do serwera. ⛔ Uprawnienie `USE_GFIX_UTILITY` **nie jest pre-sprawdzane** (dyrektywa
użytkownika): nie da się go poznać bez próby, a komunikat serwera jest konkretny i cytowalny.

### §11.6 ⭐ Read Only usunięty przy domykaniu — i to jest USUNIĘCIE, nie przeoczenie

Pierwsza wersja pokazywała stan Read Only jako wyłączony przełącznik z wyjaśnieniem. 🔒 Decyzja
użytkownika przy odbiorze: **usunąć całą sekcję**, bo *„pokazywanie kontrolki, której użytkownik nigdy nie
może użyć, nie ma wartości w V1"*. Zmierzone tło: operacja wymaga wyłączności (SQLSTATE 40001 przy jednym
otwartym attachmencie, sukces przy zerze), EmberTern trzyma 2–3 attachmenty na profil, a okno jest osiągalne
**wyłącznie po połączeniu**. ⛔ Bez workflow „rozłącz i spróbuj".

⭐ Usunięta została **cała ścieżka**, nie tylko widok: pole z DTO, **kolumna `MON$READ_ONLY` z zapytania**,
własność z VM i **trzy osierocone stałe** z `UiStrings` — bo osierocona stała przeżywa całe życie produktu
właśnie dlatego, że nikt jej nie używa (lekcja M‑3). Zasada: **nie czytamy z bazy tego, czego nie pokazujemy.**

⚠⚠ Usunięcie kolumny **przesunęło indeksy wszystkich kolumn za nią** — zmiana, która kompiluje się,
przechodzi testy i myli dane. Dlatego czytnik został **ponownie zweryfikowany na żywym FB5** po
przenumerowaniu.

⏸ Zapisane, nie rozstrzygnięte: `DatabaseApplyFailure.DatabaseInUse` straciło swoje zmierzone uzasadnienie
(było nim Read Only) i **nie jest już dowodliwie osiągalne**. Zostawione z jawnym zastrzeżeniem przy
definicji — usunięcie go jest osobną decyzją, nie konsekwencją tej zmiany.

### §11.7 Trzy poprawki UX przy domykaniu etapu

**(a) Odstępy w sekcji Configuration.** Pole `Sweep interval` i oba przełączniki stykały się i czytały jak
jeden blok. ⭐ Przyczyną była **struktura, nie brak marginesu**: siedziały w jednej trzywierszowej siatce,
więc sąsiadowały bezpośrednio. Rozdzielone na wiersz pola + blok przełączników; odstęp niesie
`Margin.FieldGap` (rola „przerwa po polu"), przełączniki `Space.Sm`. ⛔ Zero nowych tokenów, zero zmian
w globalnych wysokościach kontrolek, `settings-group` nietknięty.

**(b) ⭐⭐ Komunikat uwierzytelniania — i to jest ODWRÓCENIE zapisanej decyzji.** Surowy
`Not supported plugin 'Legacy_Auth'` jest dla użytkownika mylący: ten sam tekst niesie brak SRP, **złe
hasło** i nieistniejącego użytkownika. Nowy komunikat mówi, że serwer odrzucił uwierzytelnienie, że
EmberTern mówi wyłącznie SRP, prosi o sprawdzenie loginu i hasła oraz konfiguracji konta pod SRP i podaje
adminowi `CREATE USER … USING PLUGIN Srp`.

⚠⚠ **Podpowiedź do Legacy_Auth już raz istniała i została usunięta** — bo *orzekała o przyczynie* i myliła
się, gdy tekst pochodził od złego hasła. ⭐ Nowa jest bezpieczna dokładnie tam, gdzie tamta nie była:
**nie orzeka niczego**, więc jest prawdziwa dla każdej przyczyny, którą ten błąd obejmuje. Rozróżnienie
*naprowadza vs orzeka* pilnuje osobny strażnik.
⚠⚠ Rozpoznanie idzie **po TEKŚCIE**, czyli po tym, czego ten kod zakazuje gdzie indziej — musi, bo ten błąd
przychodzi **bez SQLSTATE i bez GDS**. Zapisane przy kodzie, nie ukryte. ⚠ Istniejący strażnik
`MapErrorMessage_LegacyAuthInMessage_ReturnsRawServerMessage` **odwrócono z uzasadnieniem**, zachowując
połowę, która nadal obowiązuje: podmiana dotyczy **wyłącznie tego jednego przypadku**.

**(c) Tło nawigacji Settings.** Panel niósł `ChromeStrongBrush` i czytał się jako za jasny wobec głównego
paska bocznego. Zmierzone: `MainWindow` maluje `SidebarPanel` **`PanelBrush`** — nawigacja ustawień bierze
**dokładnie ten zasób**, bo oba panele to ta sama rzecz: lewa nawigacja okna. ⚠ Konsekwencja świadoma:
nawigacja i treść mają teraz ten sam ton, więc granicę niesie kreska 1 px.

**(d) ⭐ Niełamliwe separatory liczb.** `Between 1 and 1 000 000.` łamało się na `…1` / `000 000.`, czyli
jedna liczba czytała się jak dwie. Nowy `EmberTern.Core.Formatting.ProseNumbers` zamienia na U+00A0
**spację mającą cyfrę po obu stronach** — reguła o KSZTAŁCIE, więc obejmuje teksty istniejące i przyszłe.
Wpięta w **jednym miejscu** (`SettingRowViewModel.Description`). ⭐ Odstępy międzywyrazowe zostają zwykłe, więc
opis nadal się zawija. ⚠ Wyszukiwanie czyta tekst **surowy**, żeby wpisanie „1 000 000" zwykłymi spacjami
nadal trafiało. Strażnik przebiega **cały katalog**, nie trzy wybrane zdania.

### §11.8 Strażniki zakresu — przeciw rozrostowi, nie przeciw błędom

- `TheWriterNeverCallsASetterRatifiedOutOfV1` — `SetSqlDialectAsync` · `SetPageBuffersAsync` ·
  `SetAccessModeAsync`. ⚠ **Wszystkie trzy działają technicznie**, więc przyszły czytelnik sterownika uznałby
  ich brak za przeoczenie. Strażnik mówi, że nim nie jest.
- `TheReaderNamesNoColumnOutsideTheRatifiedScope` — osiem kolumn FB4/FB5, asercja **negatywna** (lista
  dozwolonych byłaby przepisaniem katalogu FB3, #333).

⚠ Przy dopisywaniu okna zapaliły się **dwa istniejące strażniki z M4.4** i oba miały rację: nowe okno
`SizeToContent` musi mieć **zapisaną decyzję** o wzroście (#340 przełożone na strażnika), a okno z sufitem
musi mieć **`ScrollViewer`**, bo sufit bez przewijania nie ogranicza treści, tylko ją **przycina**. ⛔ Żadnego
nie osłabiono.

### §11.9 ⭐ Gotcha #351 odtworzona przez autora w tej samej sesji

Nowy dialog użył `Border.settings-group` na hoście `BackgroundBrush` — czyli dokładnie defekt opisany
godzinę wcześniej przy punkcie 5. Render wyglądał wiarygodnie; **pomiar pikseli** pokazał kartę `#1E1E1E`
na tle `#1E1E1E`. Naprawione **hostem**, styl karty nietknięty.
⭐ To jest argument za tym, że wpis był wart zapisania: pierwszym, który w niego wdepnął, był jego autor.

### §11.10 Weryfikacja

- Build **0/0**, zero ostrzeżeń
- Suite **8467** = 8280 (główna) + 132 (headless zgrupowana) + 55 (headless izolowana), `--blame-hang`
- ⭐ Krucha ręczna lista nazw w filtrze partycji headless **nie urosła** — nowe testy czytają ViewModele
  i źródła, więc trafiły do partycji głównej
- Smoke czysty; `Lab/` nietknięty
- Render: `dotnet run --project tools/probes/VisualCandidateProbe -- dbprops` (3 stany × 2 motywy)
- ⭐ **Czytnik i writer zweryfikowane NA ŻYWO** — pełny cykl odczyt → zapis → ponowny odczyt na FB5, na bazie
  scratch; program weryfikacyjny skasowany po przebiegu
- Strażniki zweryfikowane podsadzeniem: kolumna FB4 w czytniku · heurystyka tekstowa `Legacy_Auth` ·
  martwy NBSP · komunikat orzekający o przyczynie
