# Localization / Core + Firebird — HANDOVER do NOWEJ SESJI

⭐⭐ **To jest jedyny punkt startowy kolejnej sesji.** Przeczytaj w całości, zacznij od §10.
⛔ **Nie audytuj ponownie tego, co §3 opisuje jako zamknięte.** C0 (audyt) jest wykonany i ratyfikowany;
zamknięte i odebrane są **C1, C2, C3, C4a, C4b i C5**; **C6 DOSTARCZONE, czeka na odbiór**.
⏭ **Następny etap: `Office` ×2 (`ex.Message` jako klasa A pod postacią `throw`) — §5.4.**
⚠ C6 jest ZROBIONE: mechanizm liczby mnogiej istnieje, więc §4 czytaj jako zapis wymagań, nie jako zadanie.

Poprzedni handover — [localization-app-stage-handover.md](localization-app-stage-handover.md) — dotyczy etapu
**App** i pozostaje aktualny jako opis MECHANIZMU. ⚠ Jego §C.1 („gdzie siedzi ≈280 komunikatów") jest
**obalone pomiarem** — patrz §2.1.

---

## 1. ⚠⚠ STAN REPOZYTORIUM — PRZECZYTAJ NAJPIERW

| | |
|---|---|
| **Gałąź** | `feat/localization` |
| **HEAD** | `8f861ce25ae3c2b4a7480edcf26046cc62d1a6c5` — *„feat(localization): etap Localization/App …"* |
| **Commity w etapie Core** | ⛔ **ZERO.** HEAD nie ruszył się od zamknięcia etapu App |
| **Merge** | ⛔ **ŻADEN.** `master` nietknięty |
| **`master`** | `f5c50dc67dbc520041aac72c40b09aaaee248f0d` (merge Product Polish M0–M5 + pakiet UX) |
| **`origin`** (Gitea, HTTPS) | odczytane `git ls-remote`: `master` = `f5c50dc`, `feat/product-polish` = `bd79bc3`, `feat/localization` = `8f861ce` |
| **`private`** (GitHub, SSH) | odczytane `git ls-remote`: **identycznie** — te same trzy SHA |
| **Gałęzie zdalne** | `master`, `feat/product-polish`, `feat/localization` — nic więcej, na obu remote'ach |
| **Working tree** | ⚠ **BRUDNY** — po C6: 48 zmodyfikowanych + 19 nieśledzonych |
| **Build** | 0 błędów / 0 ostrzeżeń |
| **Smoke** | czysty, 0 wpisów `FATAL`, proces zamykany czysto |
| **`Lab/`** | ⭐ **0 zmian** po każdym etapie C1–C5 |

⭐ **Oba remote'y są zgodne z lokalnym HEAD**, czyli cała praca etapu Core istnieje **wyłącznie w drzewie
roboczym tej maszyny**.

### ⛔⛔ RYZYKO NUMER JEDEN — 14 PLIKÓW NIEŚLEDZONYCH, BEZ SIATKI BEZPIECZEŃSTWA

**Cała praca C0–C5 jest NIEZACOMMITOWANA.** Gotcha **#350** opisuje dokładnie ten stan i została zapłacona
raz: `git checkout` cofa do HEAD, czyli do stanu **sprzed całego etapu**, a plik **nieśledzony i usunięty nie
ma w gicie żadnej siatki** — puste `git log --all`, brak dangling blob, nie do odzyskania.

**Pliki, których utrata byłaby nieodwracalna:**

```
src/EmberTern.Core/Diagnostics/SessionHealthMessages.cs                 ← C1
src/EmberTern.Core/Sql/Language/QuickInfo/QuickInfoMessages.cs          ← C2
src/EmberTern.Firebird/FirebirdConnectionMessages.cs                    ← C3
src/EmberTern.Core/Settings/SettingsStoreMessages.cs                    ← C4a
src/EmberTern.Core/Settings/Export/SettingsExportMessages.cs            ← C4b
src/EmberTern.Core/Sql/Language/DiagnosticsMessages.cs                  ← C5
tests/EmberTern.Tests/SessionHealthLocalizationTests.cs                 ← C1
tests/EmberTern.Tests/QuickInfoLocalizationTests.cs                     ← C2
tests/EmberTern.Tests/FirebirdConnectionLocalizationTests.cs            ← C3
tests/EmberTern.Tests/SettingsStoreLocalizationTests.cs                 ← C4a
tests/EmberTern.Tests/SettingsExportLocalizationTests.cs                ← C4b
tests/EmberTern.Tests/DiagnosticsLocalizationTests.cs                   ← C5
docs/design/localization-core-stage-handover.md                         (ten plik)
docs/history/28-localization-core-stage.md
```

⚠⚠ **Ryzyko ROŚNIE z każdym etapem — po C5 to już SZEŚĆ odebranych etapów bez commita.**
⭐ **Rekomendacja dla pierwszej czynności nowej sesji: zaproponować użytkownikowi commit odebranych etapów
C1–C5**, zanim cokolwiek zaczniesz. ⛔ Commit wymaga decyzji użytkownika (stojąca dyrektywa „nie commituj").

⚠ **Do przeglądu diffa używaj `git diff --ignore-cr-at-eol`.** `core.autocrlf=true`, więc surowy
`git diff --stat` raportuje dla `CLAUDE.md` ~12 700 zmienionych linii, gdy realnie jest ich ~50. Zmierzone
i wyjaśnione w raporcie C1; **nie jest to uszkodzenie pliku** (drzewo robocze jest w 100 % CRLF).

### 1.1 Liczby testów i partycje (zmierzone po C5)

```
partycja GŁÓWNA      8 387   (wyklucz 20 nazw — ⛔ WYPROWADŹ je, nie przepisuj)
partycja ZGRUPOWANA    267   (te same 18 nazw bez ConnectionExpandBindingProbe/BrandingPresentationTests)
partycja IZOLOWANA      55   (ConnectionExpandBindingProbe | BrandingPresentationTests)
                    ───────
SUMA                 8 709
```

⛔⛔ **NIE przepisuj listy nazw — WYPROWADŹ ją.** Rosła co etap i była już raz nieaktualna:
CLAUDE.md notuje przebieg, w którym ręcznie przepisany filtr miał 12 nazw przy 18 w kodzie, co dało
**13 niepowodzeń niezwiązanych ze zmianą**. Po C6 nazw jest **20**:

```bash
grep -rln HeadlessCollection tests/EmberTern.Tests/*.cs | xargs -n1 basename | sed 's/\.cs$//'
```

⚠⚠ **Filtr rośnie o nazwę na moduł** (13 → 16 → 17 → **18**). Każdy test dotykający
`Loc.UseCatalogForVerification` **lub ruszający `CultureInfo.CurrentCulture`** musi dołączyć do
`HeadlessCollection` **i** do filtra. ⛔ Objaw pominięcia jest **utajony** — wyścig o globalny katalog `Loc`,
nie czerwony test w tym samym przebiegu.

⚠ **Cały wzrost C4b i C5 trafił do partycji ZGRUPOWANEJ** (185 → 196 → 207); partycja główna stoi na 8 280
przez trzy etapy z rzędu, bo nowe strażniki podmieniają katalog `Loc`.

⛔ **Kryterium zielonego przebiegu to SUMA 8 542, nie „0 niepowodzeń"** — przebieg, w którym partycja nie
wystartowała, też raportuje 0 niepowodzeń. ⚠ I mierz, nie cytuj: ta liczba starzeje się co etap.

### 1.2 ⚠ Znany flake — NIE jest problemem tego etapu

`SettingsLoadHealthTests.ConcurrentSaves_NeverLeaveSettingsUnreadable` — test `Parallel.For`, udokumentowany
w CLAUDE.md **dwukrotnie przed tym etapem**.

| Etap | Wystąpienia |
|---|---|
| C1, C3 | po jednym razie, przy 2/3 zielonych powtórzeniach partycji |
| **C4b** | **3 z 6** przebiegów partycji głównej |
| **C5** | **1 z 3** przebiegów partycji głównej |
| solo | ⭐ **zawsze zielony** (3/3 w każdym etapie) |

Objaw stały: `Assert.Empty() Failure: Collection was not empty`.

⛔ **Nie ogłaszany naprawionym ani powiązanym z żadnym etapem Core.** Ślad C4b to 7 plików, ślad C5 to 12
i **w żadnym nie ma `ApplicationSettingsStore`, `AtomicWrite` ani blokady międzyprocesowej** — czyli dokładnie
tego, co ten test ćwiczy. ⚠⚠ **Częstotliwość wygląda na wyższą niż notowana przed etapem**; odnotowuj w obie
strony, a jeśli się utrzyma — to zasługuje na własne zadanie infrastrukturalne, nie na kolejny przypis.

---

## 2. STAN LOKALIZACJI

### 2.1 ⭐⭐ Zakres — odziedziczona liczba NIE PRZETRWAŁA POMIARU

Handover etapu App podawał **≈280** komunikatów Core/Firebird. Audyt C0 wykazał, że **trzy z dziesięciu pozycji
inwentarza opisywały coś innego, niż twierdziły**:

| Pozycja | Deklarowane | Zmierzone | Dlaczego |
|---|---|---|---|
| `CharsetCatalog` | 8 | **0** | to NAZWY charsetów Firebirda, klasa **D** |
| `Core/Import/**` | ~20 | **0** | moduł **był już zbudowany poprawnie** — enum kodu, komentarz mówi *„as a code — never a message"* |
| `FirebirdDiagnostics` | 24 | **0** klasy A | klasa deklaruje o sobie *„No UI"*, pisze wyłącznie do `%TEMP%` |

🔒 **Realny zakres: ~170–190 komunikatów klasy A.** ⛔ Nie planuj z tabeli §C.1 starego handoveru.

### 2.2 Etap App — ZAMKNIĘTY (2026-08-09), stan niezmieniony przez C1–C5

| Pozycja | Stan |
|---|---|
| Wpisy w katalogu należące do App | **2 186** (nazwa = property `UiStrings`) |
| Składowe `UiStrings` | **2 186 property, ZERO pól** |
| Miejsca `{app:Loc}` w XAML | **1 263** |
| Zaszyte teksty user-visible w XAML i w App C# | ⭐ **0** |
| Zmienione wartości angielskie | ⭐ **0**, dowiedzione przeciw zrzutowi KOMPILATORA |
| Polskie tłumaczenie | ⛔ **nie istnieje** — angielski jest jedynym językiem i jednocześnie bazowym |

⛔ **188 wartości mających po kilka kluczy świadomie NIE scalono** (`"Delete"` ma 12 właścicieli, `"Cancel"`
11) — to różne pojęcia dzielące angielskie słowo, a język fleksyjny odmieni je różnie. 🔒 Ratyfikowane:
**w lokalizacji kontekst jest ważniejszy niż mechaniczna deduplikacja.**

### 2.3 Postęp etapu Core / Firebird

| Moduł | Stan | Klucze |
|---|---|---|
| `SessionHealthAnalyzer` | ✅ **C1** odebrane | 16 |
| `QuickInfoEngine` | ✅ **C2** odebrane | 18 |
| `FirebirdConnectionService` | ✅ **C3** odebrane | 4 |
| `ApplicationSettingsStore` | ✅ **C4a** odebrane | 18 |
| `Settings/Export` | ✅ **C4b** odebrane ⇒ **całe C4 domknięte** | 20 |
| `DiagnosticsEngine` (ET0001–8) | ✅ **C5** odebrane | 9 (na 8 kodów) |
| `ExecutionSummary` / `ExecutionActivity` | ✅ **C6** dostarczone, czeka na odbiór | **18** + mechanizm liczby mnogiej |
| **`Office` ×2** | ⏭ **NASTĘPNY** | 2 |
| **Performance** | ⛔ moduł ZAMKNIĘTY, poza zakresem | ~70 |

### 2.4 Stan katalogu `Strings.resx` (zmierzony po C5)

```
wpisy razem                         2 295
├─ należące do App                  2 186   (nazwa = property UiStrings, bez kropek)
├─ o kształcie Core (z kropkami)       108   (klucz MessageKey, rozwiązywany przez Loc.Format)
└─ metadane mechanizmu                   1   (Localization.PluralRuleSet — nazwany wyjątek, §7)
```

Rodziny kluczy Core: `SessionHealth.*` (16) · `QuickInfo.*` (18) · `Firebird.Connection.*` (4) ·
`Settings.Load/Refuse/Write.*` (18) · `Settings.Import.*` (20) · `Sql.Diagnostics.*` (9) ·
**`Query.Exec.*` (18 kluczy / 23 wpisy — pięć z nich to RODZINY liczby mnogiej po dwa warianty)**.

⭐ **85 zadeklarowanych kluczy == 85 wpisów o kształcie Core** — zweryfikowane pomiarem, pilnowane przez
strażniki w OBU kierunkach (§7).

### 2.5 Stan seam D‑3

`Core/Localization/MessageKey.cs` + `LocalizableMessage.cs`; App rozwiązuje przez `Loc.Format`.

- ⭐ **Producenci:** sześć klas (`SessionHealthMessages`, `QuickInfoMessages`, `FirebirdConnectionMessages`,
  `SettingsStoreMessages`, `SettingsExportMessages`, `DiagnosticsMessages`) w assembly **Core i Firebird**.
- ⭐ „Bez prozy w kontrakcie" jest wymuszone **KONSTRUKCJĄ**: `MessageKey` przyjmuje wyłącznie token
  w kształcie identyfikatora, więc żadne zdanie nie jest legalnym kluczem.
- ⭐⭐ **Od C5 `LocalizableMessage` ma RÓWNOŚĆ STRUKTURALNĄ** (`Equals`/`GetHashCode` po kluczu i argumentach
  element po elemencie). To warunek, nie ozdoba — patrz §3 i `localization.md` §4.0, gotcha **#358**.
- ⚠ **Przesłanka nowej równości:** argument musi być **wartościowo porównywalny** (`string`, liczba
  całkowita). ⛔ `byte[]`/`char[]` po cichu przywraca porównanie referencji. Strażnik:
  `DiagnosticsLocalizationTests.NoProducerPassesAnArgumentWithoutValueEquality`.
- ⚠ **Argumenty to DANE i mogą zawierać angielski** — nazwa tabeli, ścieżka, surowy komunikat Firebirda.
  To jest zamierzone i **jest właśnie sposobem, w jaki granica D‑3 jest utrzymywana**.

### 2.6 🔒 Reguła enum vs `MessageKey` (ratyfikowana w C0)

| Kształt | Kiedy |
|---|---|
| **enum kodu** | zbiór jest zamknięty i skończony, a App może chcieć się **rozgałęzić** na rodzaj |
| **`MessageKey` + argumenty** | komunikat niesie **dane dynamiczne** albo jest czysto prezentacyjny |

⛔ **Istniejących poprawnych enumów NIE migrujemy:** `ExportUnavailableReason`, `ImportDiagnosticCode`,
`ImportErrorKind`, `ImportReadiness`, `SqlLiteralWriter`. Ten mechanizm powstał przed D‑3 i robi tę samą robotę.

### 2.7 🔒 Kiedy obowiązuje wzorzec DUALNY (angielski + `Localized`)

⭐ **Kryterium jest jedno i jest POMIAROWE: czy istniejące testy przypinają DOKŁADNE brzmienie**, albo czy
istnieje niezmigrowana ścieżka, która pokazałaby użytkownikowi surowy klucz.

| Etap | Wybór | Powód (zmierzony) |
|---|---|---|
| **C3** `ConnectionFailedException` | **dualny** | wyjątek łapią ścieżki, których nikt nie wyliczył; klucz w `Message` pokazałby identyfikator. Niezmigrowana ścieżka degraduje się do **dokładnie dzisiejszego zachowania** |
| **C4a** `ApplicationSettingsStore` | **dualny** | ~20 testów przypina brzmienie odmowy na powierzchni **reguły #11** ⇒ dowodem zerowej zmiany są **nietknięte testy** |
| **C4b** `Settings/Export` | **dualny** | `SettingsExportFormatTests` przypinają dokładny tekst (sprawdzone, nie założone) |
| **C2** `QuickInfoEngine` | **zastąpienie** | zmiana typu `string` → `MessageKey`; nic nie przypinało treści |
| **C5** `DiagnosticsEngine` | **zastąpienie** | zmierzone: `DiagnosticsEngineTests` asertuje `Category`/`Code`/`Severity`/`Start`/`Length` i **ani razu treści** |

⭐⭐ **Ukryta zaleta zastąpienia, odkryta w C5:** zmiana TYPU **wylicza swoje miejsca użycia** — kompilator
znalazł dwóch konsumentów, których mój inwentarz pominął (`DebugPreflight`, `FirebirdGrammarCorpusTests`).
Forma dualna zostawiłaby oba cicho po angielsku na zawsze. ⚠ To nie znaczy „zawsze zastępuj" — znaczy, że
przy wyborze trzeba **zważyć oba koszty**.

### 2.8 🔒 Zasady live switching (D‑1: język zmienia się NA ŻYWO)

Trzy sposoby, w kolejności preferencji:

1. ⭐⭐ **Binding / property czytana przy każdym odczycie** — najlepsza, bo nie ma czego wyrejestrować, nie ma
   kolejności i nie da się zapomnieć. `{app:Loc Key}` w XAML, `static string X => Loc.Text(nameof(X))` w C#.
2. ⭐ **Poprawność przez KOLEJNOŚĆ** — komunikat składany PO tym, jak `Loc.Apply` przełączyło język
   (baner Settings Center w C4a; dialog importu w C4b, gdzie `SettingsPortability.Apply` przełącza język przed
   zwróceniem wyniku). ⚠ Zapisz rozumowanie **w miejscu**, bo wygasa przy zmianie struktury.
3. **Hak `Loc.LanguageChanged`** — tylko dla tekstu, którego binding nie dosięga.
   ⚠ **Zmierzone: w produkcie są DOKŁADNIE DWIE subskrypcje** — `MainWindowViewModel` (+ wszystkie zakładki
   przez `RaiseAllPropertiesChanged`) i `DiagnosticsPanelViewModel` (W3, C5).

⭐ **Zanim zbudujesz jakikolwiek mechanizm — ustal, czy stan JEST OSIĄGALNY** (lekcja #346). Dwa razy
odpowiedź była „nie" i to było **ustalenie, nie pominięcie**:

| Powierzchnia | Werdykt |
|---|---|
| karta Quick Info (C2) | budowana od nowa przy każdym najechaniu ⇒ zero pracy |
| karta hover diagnostyk (C5) | zdejmuje ją `PointerExited` na `TextView` **oraz** dowolne kliknięcie, a dotarcie do wiersza Language wymaga obu ⇒ **nieosiągalne** |
| dialog importu (C4b) | `ShowDialog` nad Settings Center — jedynym pisarzem preferencji języka ⇒ **nieosiągalne** |

⛔⛔ **Pułapka W3 z C5, warta zapamiętania przy każdym panelu z optymalizacją „nie przebudowuj":** oczywista
naprawa — przebudować wiersze i republikować — jest **POŻERANA przez `Unchanged()`**, bo po zmianie języka
znaleziska są te same. Hak nie może więc dotykać kolekcji; ma poprosić istniejące wiersze o ponowne
przeczytanie tekstu.

### 2.9 ⛔ Czego świadomie NIE migrujemy

| Co | Powód |
|---|---|
| `FirebirdDiagnostics` i `LogConnectionAttempt` | klasa **E** — piszą do `%TEMP%\EmberTern-debug.log`, nigdy na ekran. Tłumaczenie logu deweloperskiego utrudnia porównanie ze zgłoszeniem użytkownika |
| **Surowe komunikaty serwera Firebirda** | klasa **D** — jadą jako **argument**, dosłownie. To jest sposób utrzymania granicy D‑3 |
| **SQL, nazwy plików, identyfikatory, nazwy charsetów** | to nie język, to dane |
| `CharsetCatalog` | 8 pozycji to NAZWY charsetów Firebirda |
| `Core/Import/**` | moduł już poprawny — `ImportRowError` niesie enum, komentarz mówi *„as a code — never a message"* |
| **Strona EKSPORTU** w `Settings/Export` | zmierzone: dwa `ArgumentException` w `SettingsExporter` są **nieosiągalne** za bramką `CanExport`; opakowanie błędu jest już zlokalizowane w App |
| `SettingsLoadResult` | `readonly record struct` — `LocalizableMessage` zdegradowałby jego równość wartościową. App czyta `ConnectionProfileStore.SettingsMessage` |
| Komunikaty deweloperskie i techniczne wyjątki | granica **semantyczna, nie mechaniczna** (§3) |

⛔⛔ **Nie tłumacz tekstu tylko dlatego, że grep go znalazł.** Pytanie jest zawsze: *czy to zdanie EmberTerna
skierowane do użytkownika, czy dana / wypowiedź obcego systemu / ślad dla programisty?*

---

## 3. ⛔ DECYZJE JUŻ ZATWIERDZONE — NIE przedstawiaj ich ponownie jako pytań

| # | Decyzja | Status |
|---|---|---|
| **D‑1** | Język zmienia się **NA ŻYWO**, bez restartu | 🔒 ratyfikowana (odwrócona w trakcie etapu App z „po restarcie" — zapis historyczny, nie ciekawostka) |
| **D‑2** | **`.resx` + `ResourceManager`**, angielski jako zestaw neutralny; połowa reguły architektury #6 („no resx") **świadomie uchylona**, reszta stoi | 🔒 ratyfikowana |
| **D‑3** | Core/Firebird oddają **`MessageKey` + argumenty**; słowa rozwiązuje App | 🔒 ratyfikowana |
| — | ⛔ **Odejście od `const` i `{x:Static}`**: `const` jest inline'owany (nie ma czego rozwiązywać), `static readonly` zamarza w pierwszym języku, `{x:Static}` **nie jest bindingiem**. ✅ Dozwolone: **property** + `{app:Loc Key}` | 🔒 ratyfikowana |
| — | ⛔ **Indekser w `LocalizationSource` jest MARTWY** — ani `"Item[]"`, ani `string.Empty` nie docierają do bindingu po indekserze w Avalonii 12.1.1; wersja z indekserem **renderuje się poprawnie przy pierwszym załadowaniu**, więc awaria się chowa. Stąd jeden obiekt powiadamiający **na klucz** | 🔒 zmierzone, nie wracać |
| — | **`LocalizableMessage`** jako nośnik D‑3 | 🔒 ratyfikowana |
| — | ⭐⭐ **Strukturalna równość `LocalizableMessage`** (klucz + argumenty element po elemencie), naprawiona **u nośnika**, nie przez nośnik o stałej arności w `Diagnostic` | 🔒 ratyfikowana (C5) |
| — | ⭐ **W3 dla `DiagnosticsPanel`**: jedna subskrypcja na panel, wiersz obserwowalny ale **nie subskrybujący**, odświeżenie wyłącznie właściwości tekstowej, bez przebudowy kolekcji i bez utraty zaznaczenia | 🔒 ratyfikowana (C5) |
| — | ⛔ **`ET0008` = DWA klucze**, rzeczownik „trigger"/„function" **nie** jedzie jako argument; klucz mieszka w `Diagnostic`, bo jedna kategoria daje dwa zdania (mapa `Category → zdanie` nie jest 1:1) | 🔒 ratyfikowana (C5) |
| — | ⛔ **Zero językowych ifów** — żadnego `if (language == "pl")`, żadnego `PolishUiStrings`. Pilnuje `NoCode_BranchesOnAParticularLanguage` | 🔒 ratyfikowana |
| — | ⛔ **Core nie zna języka** — nie czyta katalogu, nie wybiera tekstu, nie referencjuje App ani Avalonii. Pilnują `Core_ReferencesNeitherAppNorAvalonia` + per-moduł „te same klucze niezależnie od języka" | 🔒 ratyfikowana |
| — | Wzorzec **dualny** tam, gdzie testy przypinają tekst (§2.7) | 🔒 ratyfikowany (C3, C4a, C4b) |
| — | **Surowy komunikat serwera zostaje surowy**, jako argument | 🔒 ratyfikowana |
| — | ⛔⛔ **`Legacy_Auth` rozpoznawany WYŁĄCZNIE po tekście SERWERA** — przepięcie na nasz zlokalizowany tekst jest w angielskim **niewidoczne** (#356) | 🔒 ratyfikowana |
| — | **Liczby idą za kulturą CZYTELNIKA** (`48,102` → `48 102`) — zachowanie oczekiwane, ⛔ nie regresja | 🔒 ratyfikowana |
| — | ⭐ **Echo pola technicznego** (wersja formatu, licznik iteracji KDF) jedzie jako **string invariantny**, bo specyfikator formatu staje się wtedy bezczynny; liczba, którą czytelnik **liczy**, idzie za jego kulturą | 🔒 ratyfikowana (C4b, #357) |
| — | `SettingsLoadResult` **bez** `LocalizableMessage` (value equality) | 🔒 ratyfikowana |
| — | **Nie scalamy 188 wartości o wielu kluczach** — kontekst > deduplikacja | 🔒 ratyfikowana |
| — | `FirebirdDiagnostics`, logi, SQL, nazwy plików, nazwy charsetów — **poza zakresem** | 🔒 ratyfikowana |
| — | ⛔ **Nie ruszać odłożonych modułów bez osobnej decyzji**: Performance, `KindLabel`/`SymbolKind`, Data Import | 🔒 ratyfikowana |
| — | **Granica `ex.Message` jest SEMANTYCZNA, nie mechaniczna** — techniczny wyjątek zostaje surowy; wyjątek świadomie niosący komunikat dla użytkownika i trafiający wprost do UI jest kandydatem | 🔒 ratyfikowana (C0) |
| — | **Kolejność migracji modułów** (§2.3) | 🔒 ratyfikowana (C0) |
| — | **Kolejność pracy per moduł:** audyt → propozycja → akceptacja → implementacja → testy → build → weryfikacja | 🔒 ratyfikowana od C1 |

---

## 4. ⏭ C6 — `ExecutionSummary` / `ExecutionActivity` — PUNKT STARTOWY

**~15 komunikatów.** ⛔⛔ **NAJPIERW TYLKO AUDYT I PROPOZYCJA KONTRAKTU. Nie implementuj żadnego
mechanizmu.**

### 4.1 Dlaczego C6 jest szczególny

Obecny kod **skleja całe zdania**:

```
string.Format("{0} {1} {2}", n, n == 1 ? "row" : "rows", verb)     →  "8 rows inserted"
```

Klucz-za-klucz **nie wystarczy**, i to z czterech niezależnych powodów:

1. **Szyk zdania może się zmienić** w innym języku — kluczowanie `"row"`, `"rows"` i `"inserted"` osobno
   przypina kolejność, której inny język nie musi mieć.
2. **Polski ma trzy istotne formy liczby** (1 wiersz / 2–4 wiersze / 5+ wierszy — plus reguła dla końcówek
   22, 23, 24…), więc `n == 1 ? a : b` jest strukturalnie niewystarczające.
3. ⛔ **Nie wolno robić `if (language == "pl")`** — pilnuje `NoCode_BranchesOnAParticularLanguage`.
4. Mechanizm ma być **użyteczny również dla kolejnych języków**, dodawanych bez zmiany kodu (§2.8, D‑1/D‑2).

### 4.2 ⚠ Zmierzone ograniczenie mechanizmu

Strażnik `NoCode_BranchesOnAParticularLanguage` skanuje `App/Localization/**`, więc **tablica reguł
per-język go zapali**. ⇒ Mechanizm będzie musiał deklarować „rodzinę reguł" jako **DANĄ**, nie jako gałąź
kodu. ⛔ To ograniczenie **nie jest powodem do osłabienia strażnika** — jeśli zapali się na poprawnej
zmianie, najpierw sprawdź, czy jego przesłanka nadal jest prawdziwa, i **pokaż problem użytkownikowi**
(tak powstał podział katalogu w C1).

### 4.3 ⛔ Czego nowa sesja MA zrobić, zanim napisze linię kodu

Kolejność jest polecona przez użytkownika i **nie jest sugestią**:

1. **Zinwentaryzować WSZYSTKIE przypadki pluralizacji** w produkcie (nie tylko w C6 — patrz licznik §5.1).
2. Wskazać **wszystkie miejsca PRODUKCJI i KONSUMPCJI** komunikatów `ExecutionSummary` / `ExecutionActivity`.
3. Pokazać **obecny kontrakt publiczny** obu typów.
4. Sprawdzić **istniejące testy** (czy przypinają brzmienie ⇒ §2.7 decyduje dualny vs zastąpienie).
5. Sprawdzić **istniejące strażniki dotyczące języka** i to, które z nich mechanizm zapali.
6. Określić, czy problem dotyczy **wyłącznie pluralizacji, czy również SKŁADANIA ZDAŃ** (podejrzenie: obu —
   ale to trzeba zmierzyć, nie założyć).
7. Zaproponować mechanizm **niezależny od konkretnego języka**.
8. Pokazać, jak mechanizm zadziała **dla EN i dla PL**.
9. Pokazać, jak będzie wyglądało **dodanie kolejnego języka** (⚠ dziś to **TRZY kroki**, nie dwa — §5.4).
10. Wskazać **wpływ na istniejące kontrakty i value equality** (⚠ `LocalizableMessage` ma teraz równość
    strukturalną — mechanizm nie może jej złamać; §2.5).
11. Zaproponować **testy i strażniki**.
12. **Zatrzymać się i poczekać na akceptację użytkownika.**

⛔ Nie zaczynaj C6 od kodowania. ⛔ Nie podejmuj za użytkownika decyzji projektowych. ⛔ Nie otwieraj przy
okazji Performance, `KindLabel`/`SymbolKind` ani innych odłożonych tematów.

---

## 5. TEMATY OTWARTE — faktycznie otwarte, oddzielone od decyzji już podjętych

⚠ **Wszystko z §3 jest ZAMKNIĘTE.** Poniżej jest to, co naprawdę zostaje do rozstrzygnięcia.

### 5.1 ⏸ Mechanizm pluralizacji — OTWARTY, przedmiot C6

🔒 **Decyzja, która JUŻ padła:** *nie projektować mechanizmu per moduł* — zbieramy przypadki i projektujemy
raz, na pełnym zestawie. ⏭ **C6 jest tym momentem.**

**Licznik przypadków: 5** (C5 nie dodało ani jednego):

| # | Przypadek | Skąd |
|---|---|---|
| 1–2 | `SessionHealthVerdict.Headline` ×2 | C1 — ⛔ **świadomie zostaje `string`iem**, decyzja użytkownika: nie projektować mechanizmu dla dwóch komunikatów |
| 3 | `"1 column"` / `"N columns"` | C2 (QuickInfo) |
| 4–5 | `ExecutionSummary` „1 row" / „N rows" | ⏭ **C6** |

⭐ **`ET0006` (INSERT count mismatch) NIE jest przypadkiem pluralizacji** — angielski hedge'uje
`column(s)` / `value(s)`, więc jest przetłumaczalny bez mechanizmu. Świadomie nie dopisany do licznika.

⚠ **Konsekwencja dla C6:** rozstrzygnięcie mechanizmu **domyka też przypadki 1–3**, ale ⛔ **czy je migrować
w C6, czy osobno — to decyzja użytkownika**, nie automatyczne rozszerzenie zakresu.

### 5.2 ⏸ `KindLabel` / `SymbolKind` — ODŁOŻONE decyzją użytkownika (2026-08-10)

~8 wartości faktów QuickInfo będących **NASZYMI** słowami (`KindLabel` ×3, `"Variable"`, `"Active"/"Inactive"`,
`"Identity"/"Computed"`, `"Input parameter"`).

⛔ **Nie otwierać przy okazji innego etapu** — to decyzja **kontraktowa** (`QuickInfoFact`), nie sprzątanie.
⭐ Kierunek jest znany: Core przestaje produkować SŁOWO i oddaje `SymbolKind` jako **daną**.
⛔ **Nie** deklarować kluczy rodzajów w Core — byłaby to **piąta kopia** słownictwa `ObjectKind*`, które etap
App już skonsolidował i lokalizuje.
⚠ **Koszt bieżący, przyjęty świadomie:** polski czytelnik zobaczy *„Rodzaj: Table"*, gdy drzewo metadanych
nazywa to samo *„Tabela"*.

### 5.3 ⛔ Performance (~70 komunikatów) — moduł ZAMKNIĘTY

Otwierany **dopiero po osobnej decyzji użytkownika**, po zdobyciu wzorca na modułach otwartych.
⭐ Zmierzone i warte zapamiętania: `FindingGuidanceCatalog` jest **czystą funkcją `FindingKind`**, więc dałby
się zmigrować **bez** zmiany kontraktu `Finding`. ⛔ To nie jest zaproszenie — to notatka na moment decyzji.

### 5.4 ⏸ Pozostałe granice migracji

| Temat | Stan |
|---|---|
| **`Office` ×2 / `ex.Message`** | ⏸ po C6. `XlsImportProvider` / `XlsxImportProvider` rzucają `InvalidDataException` z życzliwą podpowiedzią, którą Data Import renderuje przez `SetStatus(ex.Message)` — **klasa A pod postacią `throw`**. Granica semantyczna już ratyfikowana (C0) |
| **Polskie tłumaczenie** (2 271 wpisów) | ⏸ dopiero po migracji producentów |
| **QA wzrokowe na żywym oknie** | ⏸ ⚠ niewykonalne przy jednym języku: żywy i zamrożony binding renderują ten sam tekst |
| ⚠⚠ **Dodanie języka ma TRZY kroki, nie dwa** | (1) wiersz w `PreferenceOptions.Language`, (2) ⭐ **ETYKIETA języka** w mapie opcji wiersza `SettingLanguage` w `SettingsCatalog`, (3) plik `Strings.<kultura>.resx`. ⚠ Krok 2 został **odkryty pomiarem** — bez niego 36 testów pada z `KeyNotFoundException`; złapał to istniejący `EveryEnumeratedOptionHasALabel` |
| ⚠ **Re-kompozycja banera settings-health w `MainWindow`** | **nie jest przypięta testem** — `MainWindowViewModel` nie jest konstruowalny headlessowo bez znanego ryzyka zawieszenia. Zapisane wprost jako ograniczenie C4a |
| ⚠ **Flake `ConcurrentSaves_…`** | §1.2 — kandydat na własne zadanie infrastrukturalne, jeśli częstotliwość się utrzyma |

---

## 6. Zamknięte etapy i ich najważniejsze decyzje (HISTORIA — nie upraszczać)

### C0 — audyt (odebrany)

- 🔒 Reguła **enum vs `MessageKey`** (§2.6), 🔒 kolejność modułów (§2.3), 🔒 granica `ex.Message` (§3).
- ⭐⭐ Wynik główny: odziedziczone „≈280" **nie przetrwało pomiaru** (§2.1) — i za każdym razem korekta
  zmieniała **KSZTAŁT** pracy, nie tylko liczbę.
- ⭐ Zmierzono cztery ograniczenia, które ukształtowały cały etap: value equality `Diagnostic` ·
  konkatenacja w `ExecutionSummary` · `ex.Message` jako kanał na ekran · strażnik skanujący tylko jedno
  assembly (naprawiony prewencyjnie w C1).

### C1 — `SessionHealthAnalyzer` (odebrany)

- `SessionHealthFinding` → `LocalizableMessage`, 16 kluczy, dane jako argumenty.
- ⛔ **`SessionHealthVerdict.Headline` ZOSTAJE `string`iem** — decyzja użytkownika: nie projektować mechanizmu
  liczby mnogiej dla dwóch komunikatów.
- ⭐ **Katalog ma DWÓCH właścicieli** ⇒ strażnik **PODZIELONY, nie osłabiony**; dyskryminatorem jest
  **refleksja po polach `MessageKey`**, nie konwencja kropki. Obie partycje strzeżone w OBU kierunkach.
- 🔒 `48,102` → `48 102` to zachowanie **OCZEKIWANE**. ⛔ Nie cofać do grupowania invariantnego.
- ⭐ Ujawniony defekt **WCZEŚNIEJSZY** (#353): łańcuch zmiany języka kończył się na `WorkspaceTabViewModel`.

### C2 — `QuickInfoEngine` (odebrany)

- ⭐ Migracja okazała się **PODZIAŁEM, nie sweepem**: etykieta (18 kluczy) jest nasza, **wartość zostaje
  dosłowna**, bo to słownictwo Firebirda (`NOT NULL`, `PRIMARY KEY`, `BEFORE INSERT`) — karta, która by je
  przetłumaczyła, **nie zgadzałaby się z DDL-em, który opisuje**.
- ⭐ Znalezisko **#355**: zmiana `string` → `MessageKey` **nie zepsuła miejsca, które go SKLEJAŁO**
  (`ToString()`); łapie to wyłącznie strażnik czytający **zrealizowaną kontrolkę**.
- ⭐ Live switching nie wymagał instalacji — karta budowana od nowa przy każdym najechaniu (zmierzone).
- ⏸ Zostawiło otwarte pytanie `KindLabel`/`SymbolKind` (§5.2).

### C3 — `FirebirdConnectionService` (odebrany)

- ⭐ **Wzorcowy D‑3, cała granica w jednej linii: nasze zdanie to KLUCZ, zdanie serwera to ARGUMENT.**
- ⭐ `ConnectionFailedException` niesie **oba** kształty: `Localized` dla UI, angielski `Message` dla logów
  i nieprzemigrowanych ścieżek — nieznana ścieżka degraduje się do **dokładnie dzisiejszego zachowania**.
- ⛔⛔ **`Legacy_Auth` rozpoznawany wyłącznie po surowym komunikacie SERWERA** — przypięte **pod zmianą
  języka** (#356), bo w angielskim awaria jest **niewidoczna**.
- ⭐ Rozszerzenie strażnika o assembly **Firebird** (prewencyjnie w C1) zadziałało bez żadnej zmiany.

### C4a — `ApplicationSettingsStore` (odebrany)

- ⭐ **Wzorzec dualny**, 18 kluczy w dwóch rodzinach (`Load.*` / `Refuse.*` / `Write.*`) **świadomie NIE
  scalonych** — kilka par opisuje tę samą przyczynę i wciąż czyta się inaczej, bo odpowiada na inne pytanie
  w innym momencie.
- ⭐⭐ **Dowodem zerowej zmiany treści są NIETKNIĘTE testy** (~20) — najmocniejszy dowód w całym etapie.
- ⛔ `SettingsLoadResult` **NIE dostał komunikatu** (value equality, pułapka z C0).
- ⭐ Live switching: baner Settings Center był **JUŻ poprawny — przez KOLEJNOŚĆ**; baner settings-health
  w `MainWindow` był zamrożony i dostał jeden składacz wołany z haka języka.
- ⚠ Ograniczenie zapisane wprost: re-kompozycja tego drugiego banera **nie jest przypięta testem**.

### C4b — `Settings/Export` (odebrany) ⇒ CAŁE C4 DOMKNIĘTE

- **20 kluczy**; wzorzec dualny na trzech typach wyniku.
- ⭐ **Dwie odmowy store'a są PRZEWLECZONE, nie powtórzone** (`CanSave(out,out)` + `LastSaveMessage` z C4a) —
  dialog importu pokazuje **zdanie store'a z klucza store'a**. Drugi klucz byłby dwiema odpowiedziami na
  jedno pytanie.
- ⭐ **Rodzina `Damaged*` = CZTERY CAŁE ZDANIA**; ⭐ angielska połowa nadal **SKŁADANA** tym samym prefiksem,
  żeby strażnik równości dowodził, że wpis zasobu **odtwarza konkatenację**, a nie że ktoś przepisał zdanie.
- ⛔ **Strona EKSPORTU poza zakresem — zmierzone**, nie pominięte.
- ⚠ **Jeden klucz nieosiągalny** (`NoMigrationStep` — ladder wymaga `Oldest < Current`, oba = 1): nazwany
  wyjątek z **przypiętą PRZESŁANKĄ**, nie z wymówką.
- ⭐ Live switching bez haka — poprawne przez **kolejność i modalność**; ⛔ rozumowanie wygasa, gdyby dialog
  przestał być modalny.
- ⚠⚠ **Znalezisko #357, i najważniejsza jest droga:** twierdziłem (i zaraportowałem jako pomiar), że
  `CurrentCulture` pogrupuje argument `2000000000`. **Podsadzenie argumentu liczbowego nie zapaliło NICZEGO** —
  `{0}` z `int` **nie** stawia separatorów grup. Prawdziwą dźwignią jest **`{0:N0}` w wartości zasobu**, czyli
  coś, co dopisze **TŁUMACZ**. ⭐ Test też musiał się zmienić:
  `TheEnglishAndLocalizedForms_AgreeOnAnyCulture` wyglądał rygorystycznie i **nie mierzył tego, co obiecywał**;
  przypadek liczbowy pilnuje teraz test podający **wrogi szablon `{0:N0}`**.

### C5 — `DiagnosticsEngine` (odebrany)

- **9 kluczy na 8 kodów**; ⭐ **kształt kontraktu ratyfikowany PRZED napisaniem kodu** (polecenie użytkownika)
  — i to była właściwa kolejność.
- ⭐⭐ **Warunkiem było nadanie `LocalizableMessage` RÓWNOŚCI STRUKTURALNEJ.** Naprawione **u nośnika**, nie
  przez kontuzjowanie `Diagnostic` nośnikiem o stałej arności (sufit arności = defekt zaplanowany na później).
  ⭐ Bezpieczne, bo **zmierzone**: zero konsumentów równości tego typu ⇒ zmiana mogła tylko zrównać więcej
  rzeczy, nigdy mniej. Gotcha **#358**; architektura: `localization.md` §4.0.
- ⭐⭐ **Podsadzenie A jest najmocniejszym wynikiem etapu:** powrót do równości referencyjnej zapala **7
  testów, w tym DWA ISTNIEJĄCE** (`Update_WithUnchangedDiagnostics_DoesNotRebuildTheCollection`,
  `…_KeepsTheSelection`) — ochrona nie jest czymś, co C5 wymyśliło, tylko czymś, co **zachowało**.
- ⭐ **Bez bliźniaka angielskiego** — zmierzone (§2.7). Dowód zerowej zmiany: **diff pusty, 9 == 9**.
- ⛔ **`ET0008` = dwa klucze**; klucz mieszka w strukturze, bo `Category → zdanie` **nie jest 1:1**.
- ⛔ **`ET0004` jest NIEOSIĄGALNE** — żadna ścieżka bindera nie tworzy nierozwiązanej referencji o roli
  `Parameter`. ⭐ Potwierdzone niezależnie: **w całym suite nie ma testu ET0004** i nikt tego nie zauważył.
  Nazwany wyjątek z przypiętą przesłanką (sześć kształtów PSQL).
- ⭐ **Live switching W3** + pułapka `Unchanged()` (§2.8).
- ⭐ **Hover bez mechanizmu — zmierzone jako NIEOSIĄGALNE.**
- ⚠⚠ **Inwentarz z propozycji pominął DWÓCH konsumentów** — oba znalazł **kompilator** (§2.7).

---

## 7. Strażniki

### 7.1 Strażniki mechanizmu (etap App, nadal obowiązują)

`TheEnglishResourceSet_Loads` · `EnglishBase_ResolvesEveryKeyItDeclares` ·
`NoShippedCulture_IntroducesAKeyEnglishLacks` · `EveryLocalizedMember_MatchesItsEnglishEntry` ·
`NoLocalizedMember_IsInlinedByTheCompiler` · `AnUnusableLanguage_FallsBackToEnglish` ·
`TheLanguage_ComesOnlyFromThePreference` · ⛔ `NoCode_BranchesOnAParticularLanguage` ·
`EveryLanguageInTheCatalog_ResolvesToItsOwnCultureWithNoCodeChange` ·
`NoViewOrViewModel_ReadsTheLanguagePreference` · `Core_ReferencesNeitherAppNorAvalonia` ·
`AMessageKey_RefusesProse` / `_AcceptsAnIdentifier` · `ACoreMessage_ResolvesToEnglishTextInTheAppLayer` ·
⭐ `ABoundString_RereadsWhenTheLanguageChanges` (pomiar, na którym stoi D‑1) ·
`AUiStringsMember_ReadsTheCurrentLanguage` · `LanguageChanged_FiresForCaptureOnceConsumers` ·
`EveryLocKeyInXaml_ExistsInTheCatalog` · `NoViewCarriesAHardcodedUserVisibleString` ·
`NoCodeBuiltColumn_AssignsALocalizedHeader` · `NoField_CapturesALocalizedString`.

### 7.2 Strażniki dodane / zmienione w etapie Core

| Strażnik | Rola |
|---|---|
| `EveryCoreMessageKey_HasAnEnglishEntry` | ⭐ **samo-uzbrajający się**; skanuje assembly **Core I Firebird** (rozszerzone w C1) |
| ⭐ `EveryCoreShapedEntry_IsDeclaredByCore` | **nowy w C1** — wpis Core, którego nikt nie deklaruje, jest sierotą |
| `EveryLocalizedMember_MatchesItsEnglishEntry` | **podzielony** wg właściciela; klucze Core pomijane (mają własną połówkę) |
| `SessionHealthLocalizationTests` (4) | brak prozy · klucze się rozwiązują · dane przeżywają · **karta podąża za językiem** |
| `QuickInfoLocalizationTests` (5) | podział etykieta/wartość · ⭐ **render zrealizowanej kontrolki** (jedyny łapiący #355) |
| `FirebirdConnectionLocalizationTests` (9) | tekst serwera dosłownie · zgodność angielskiego fallbacku · ⭐ **`Legacy_Auth` pod zmianą języka** |
| `SettingsStoreLocalizationTests` (2) | ⭐ zgodność 18 odmów przez **prawdziwe scenariusze** = jednocześnie dowód zerowej zmiany treści |
| `SettingsExportLocalizationTests` (11) | 19 z 20 kluczy przez prawdziwe scenariusze · pokrycie kluczy · ⭐ **immunność argumentu na specyfikator formatu** (#357) · całe zdania `Damaged*` · przypięta przesłanka wyjątku · ⭐ **liveness dialogu App** |
| `DiagnosticsLocalizationTests` (11) | ⭐⭐ **równość wartościowa nośnika i `Diagnostic`** (na tym stoi cały kontrakt) · brak przebudowy kolekcji przez PRAWDZIWY silnik · ⭐ **live switching W3 bez utraty zaznaczenia** · dwa klucze ET0008 bez rzeczownika · ⭐ **argument bez równości wartościowej** · przypięta przesłanka nieosiągalnego ET0004 |

⭐ **Wszystkie kluczowe strażniki zweryfikowane PODSADZENIEM.** Najciekawsze trzy:
podsadzenie #355 kompiluje się z **0 błędów** i zapala **wyłącznie** strażnik renderu ·
w C4b podsadzenie argumentu liczbowego **nie zapaliło NICZEGO** i właśnie to obaliło przesłankę, na której
napisano test (#357) · w C5 podsadzenie równości referencyjnej zapala **7 testów, w tym dwa istniejące**.

⛔ **Nie osłabiaj strażnika, który zapalił się na poprawnej zmianie** — najpierw sprawdź, czy jego
**przesłanka** nadal jest prawdziwa, i pokaż problem użytkownikowi (#322, #333).

---

## 8. Nowe gotchy z tego etapu

| # | Treść w skrócie |
|---|---|
| **#353** | Rozgłoszenie docierające do POJEMNIKA nie dociera do VM jego treści — a przy jednym języku defekt renderuje się bezbłędnie aż do pierwszego przełączenia |
| **#354** | Przeniesienie miejsca wywołania na wspólny formater przenosi też decyzję o KULTURZE liczb, przy zerowej zmianie znaków |
| **#355** | Zmiana typu `string` → struct **nie psuje** miejsca, które go SKLEJA (`ToString()`); łapie to tylko strażnik czytający zrealizowaną kontrolkę |
| **#356** | Lokalizacja opakowania zagraża każdemu kodowi ROZPOZNAJĄCEMU błąd po tekście — po zmianie są dwa teksty i tylko jeden jest nadal obcego systemu |
| **#357** | Strażnik porównujący DWIE REPREZENTACJE jednego zdania jest przenośny tylko na tyle, na ile identyczne jest formatowanie argumentów — a wersja mechanizmu, którą zapisano najpierw, była błędna: `{0}` z `int` **nie** grupuje; grupuje `{0:N0}` w wartości zasobu, czyli coś, co dopisze TŁUMACZ. ⭐ Lekcja o teście: strażnik napisany pod niezreprodukowane zagrożenie może nie mierzyć niczego — pokazało to dopiero podsadzenie, które NIE zapaliło |
| **#358** | Równość generowana rekordu porównuje składową KOLEKCYJNĄ po referencji, więc typ wyglądający na wartościowy przestaje nim być — a szkoda ląduje nie na nim, lecz na typie WARTOŚCIOWYM, który go potem osadzi. ⭐ Naprawiaj u NOŚNIKA, nie kontuzjując konsumenta; ⚠ i pilnuj przesłanki: każdy element musi sam być wartościowo porównywalny |

Katalog: **345 wpisów, #1–#358.**

---

## 9. Dokumenty — źródła prawdy

| Dokument | Rola |
|---|---|
| ⭐⭐ **ten plik** | punkt startowy nowej sesji |
| ⭐ **[../history/28-localization-core-stage.md](../28-localization-core-stage.md)** | **narracja etapu, etap po etapie: C0, C1, C2, C3, C4a, C4b, C5** — czytaj dla „dlaczego" |
| [localization.md](../../design/localization.md) | architektura mechanizmu: §2.1 (martwy indekser) · §4.0 (równość strukturalna nośnika) · §4.1 (dwaj właściciele katalogu) · §4.2 + §4.2a (liczby wg kultury vs echo) · §7 (co otwarte) |
| [localization-app-stage-handover.md](localization-app-stage-handover.md) | etap **App**; ⚠ §C.1 obalone pomiarem — nie planuj z niego |
| `CLAUDE.md` → „Current state" | skrót stanu; ⚠ licznik testów w prozie starzeje się co etap — **mierz, nie cytuj** |
| `docs/gotchas.md` | **#353–#358** z tego etapu |

---

## 10. ⏭ PUNKT STARTOWY NOWEJ SESJI

### Krok 0 — zanim cokolwiek zaczniesz

1. ⚠⚠ **Zapytaj użytkownika o commit odebranych etapów C1–C5** (§1: 14 plików nieśledzonych bez siatki
   bezpieczeństwa, gotcha #350 — SZEŚĆ odebranych etapów bez commita).
2. Zweryfikuj stan: `git status`, `dotnet build EmberTern.slnx`, trzy partycje (§1.1).
   ⛔ Kryterium zielonego przebiegu to **suma 8 542**, nie „0 niepowodzeń".
   ⚠ Znany flake `ConcurrentSaves_…` (§1.2) — sprawdź solo, zanim cokolwiek ogłosisz.

### Krok 1 — C6: audyt + propozycja kontraktu

**Pełna lista dwunastu rzeczy do zrobienia PRZED kodem: §4.3.**
⛔⛔ **Zatrzymaj się po propozycji i poczekaj na akceptację.**

### Czego NIE robić w kolejnej sesji

- ⛔ **nie implementować mechanizmu pluralizacji przed ratyfikacją kształtu** (sam mechanizm jest przedmiotem
  C6 — zakazane jest zaczynanie od kodu, nie zajmowanie się tematem),
- ⛔ nie otwierać `KindLabel`/`SymbolKind` (§5.2), Performance (§5.3) ani Data Import,
- ⛔ nie migrować przypadków pluralizacji 1–3 (§5.1) bez osobnej decyzji użytkownika,
- ⛔ nie scalać `feat/localization` do `master` bez wyraźnego polecenia,
- ⛔ nie commitować, nie pushować, nie merge'ować bez polecenia,
- ⛔ nie osłabiać strażników (§7.2),
- ⛔ nie tłumaczyć tekstu technicznego, SQL, nazw plików ani logów deweloperskich tylko dlatego, że grep je
  znalazł (§2.9).
