# 27 — Pakiet UX po M5 (2026-08-09 →)

Sześć zgłoszeń ze zwykłego używania aplikacji, zebranych po zamknięciu Product Polish M5.
Gałąź `feat/product-polish` (⛔ nadal **nie** scalona do `master`).

Zakres całego pakietu, w kolejności ratyfikowanej przez użytkownika:

| # | Temat | Stan |
|---|---|---|
| 1 | Security Manager — ikona zakładki | ✅ zamknięte, odebrane |
| 2 | `Button.primary` — tekst w stanach | ✅ zamknięte, odebrane |
| 3 | Live DDL — kolorowanie na pięciu powierzchniach | ✅ zamknięte, odebrane |
| 4 | Performance — Execution plan (layout + kolorowanie drzewa) | ⏭ następne |
| 5 | Settings UX | ⏸ |
| 6 | Database Properties (nowa funkcja) | ⏸ osobny mini-etap |

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

## §6 Weryfikacja (pakiet 1–3)

- Build **0/0**
- Suite **8401** = 8218 (główna) + 128 (headless zgrupowana) + 55 (headless izolowana), wszystkie
  `--blame-hang`, wszystkie zielone
- ⭐ **Krucha ręczna lista nazw w filtrze partycji headless nie urosła** — oba nowe testy headless
  dołączyły do istniejącej klasy `DesignTokenApplicationTests`
- Smoke czysty
- Render QA obu motywów: `tools/probes/VisualCandidateProbe -- qa123`
- **Wszystkie pięć nowych strażników zweryfikowane podsadzeniem**
- QA wizualne użytkownika: **przyjęte bez uwag**
