# Localization — handover do NOWEJ SESJI

⭐⭐ **To jest jedyny punkt startowy kolejnej sesji.** Przeczytaj go w całości i zacznij od §D.
⛔ Nie audytuj ponownie tego, co §B opisuje jako zamknięte.

---

## A. Stan wejściowy

| | |
|---|---|
| **Gałąź** | `feat/localization` |
| **Odgałęziona od** | `master` @ `f5c50dc` (merge Product Polish M0–M5 + pakiet UX po M5) |
| **HEAD** | commit zamykający etap: `feat(localization): etap Localization/App …` — ⚠ hasz odczytaj przez `git log -1`, celowo NIE jest tu wpisany: dokument jest CZĘŚCIĄ tego commita, więc wpisanie hasza zmienia hasz (próbowano, pętla) |
| **Remote'y** | `origin` (Gitea, HTTPS) · `private` (GitHub, SSH) — **oba mają tę gałąź** |
| **Working tree** | czysty po commicie |
| **Build** | 0 błędów / 0 ostrzeżeń (`dotnet build EmberTern.slnx`) |
| **Suite** | **8 499** zielonych |
| **Smoke** | start czysty, zero `FATAL` |
| **`Lab/`** | nietknięty |

⚠⚠ **PARTYCJE TESTOWE ZMIENIŁY SIĘ W TYM ETAPIE — filtr ma dwie nazwy więcej.** Powód w §B.7.

```
partycja GŁÓWNA      8 280   (wyklucz 12 nazw niżej)
partycja ZGRUPOWANA    164   (te same 10 nazw bez ConnectionExpandBindingProbe/BrandingPresentationTests)
partycja IZOLOWANA      55   (ConnectionExpandBindingProbe | BrandingPresentationTests)
```

Nazwy do wykluczenia w partycji głównej: `ConnectionExpandBindingProbe`, `SettingsCenterViewTests`,
`BrandingPresentationTests`, `DesignTokenApplicationTests`, `TabStripPresentationTests`,
`MetadataTreeVirtualizationProbe`, `SharedContextMenuFeasibilityProbe`, `EditableGridEnterTests`,
`GridDateEditorTests`, `DependencyTreeRenderTests`, **`LocalizationMechanismTests`**,
**`LocalizationLivenessTests`**.

⛔ **Nie „posprzątaj" ostatnich dwóch z filtra** — §B.7 wyjaśnia, dlaczego tam są.

---

## B. Co zostało zamknięte

Pełny opis architektury: **[localization.md](localization.md)**. Tu tylko to, co musisz wiedzieć, żeby
kontynuować.

### B.1 Ratyfikowane decyzje

| # | Decyzja |
|---|---|
| **D‑1** | Język zmienia się **NA ŻYWO**, bez restartu |
| **D‑2** | `.resx` + `ResourceManager`; angielski = zestaw neutralny (bazowy); ⛔ połowa reguły architektury #6 zakazująca `.resx` **świadomie uchylona**, reszta reguły stoi |
| **D‑3** | Core/Firebird oddają **`MessageKey` + argumenty**; słowa rozwiązuje App. Surowe komunikaty **serwera** mogą zostać surowe; **nasze** opakowania — nie |

⚠ D‑1 zostało **odwrócone w trakcie** (najpierw „po restarcie"). Skutek był jakościowy: `{x:Static}`
i `static readonly` przestały wystarczać. ⭐ Wersja żywa okazała się **prostsza** — zniknęła pułapka
kolejności inicjalizacji.

### B.2 Mechanizm — pięć plików

| Plik | Rola |
|---|---|
| `App/Localization/Strings.resx` | **2 186 wpisów**, angielski bazowy. **Klucz = nazwa składowej `UiStrings`** |
| `App/Localization/Loc.cs` | jedyny resolver; `Text(key)` rozwiązuje **w chwili wywołania** |
| `App/Localization/LocalizationSource.cs` | jeden mały obiekt powiadamiający **na klucz** |
| `App/Localization/LanguagePreference.cs` | klucz preferencji → `CultureInfo` (odpowiednik `ThemePreference`) |
| `App/LocMarkup.cs` | `{app:Loc Key}` — zwraca `Binding`, nie string |

⭐ Wpięcie: `App.OnFrameworkInitializationCompleted` — obok motywu, na tej samej `PreferencesService`.

### B.3 ⚠⚠ Indekser jest MARTWY — nie wracaj do niego

Pierwsza wersja `LocalizationSource` to był standardowy wzorzec: **jeden obiekt z `this[key]`**. Zmierzone na
prawdziwym `TextBlock`: wartość początkowa binduje się poprawnie, a po zmianie języka kontrolka pokazuje
**stary tekst**. Ani `"Item[]"` (konwencja WPF), ani `string.Empty` nie docierają do bindingu po indekserze
w Avalonii 12.1.1.

⛔ **Nie „upraszczaj" tego z powrotem** — wersja z indekserem renderuje się poprawnie przy pierwszym
załadowaniu, więc awaria się chowa.

### B.4 Trzy formy składowej — tylko jedna dopuszczalna

| Forma | Kiedy się rozwiązuje | |
|---|---|---|
| `const` | inline'owana przez kompilator | ⛔ nie ma czego rozwiązywać |
| `static readonly` | raz, przy inicjalizacji typu | ⛔ renderuje poprawnie i **zamarza w pierwszym języku** |
| `static string X => Loc.Text(nameof(X))` | przy każdym odczycie | ✅ |

`UiStrings`: **2 186 property, ZERO pól**. XAML: **1 263** miejsc `{app:Loc}`, **0** `{x:Static app:UiStrings}`.

⚠ Koszt zamiany, powiedziany wprost: **straciliśmy sprawdzanie klucza przez kompilator**. Rekompensuje to
`NoViewCarriesAHardcodedUserVisibleString` + strażniki katalogu. ⛔ Nie usuwać ich.

### B.5 Powierzchnie „capture-once" — binding pobił subskrypcję

| Klasa | Rozwiązanie |
|---|---|
| Tekst, który VM **wylicza raz i publikuje** | **subskrypcja** `Loc.LanguageChanged` → `MainWindowViewModel` + każda otwarta zakładka (`RaiseAllPropertiesChanged`) |
| Kolumny `DataGrid` budowane w kodzie | **binding** `LocalizedColumn.Header` (`DataGridColumn.HeaderProperty` jest `StyledProperty`) |
| Wiersz IntelliSense | **usunięty cache** — `SqlCompletionData` rozwiązuje opis rodzaju we właściwości |

⭐ Binding jest lepszy od subskrypcji: nie ma czego wyrejestrować, nie ma kolejności, nie da się zapomnieć.
Subskrypcja per wiersz listy uzupełniania byłaby wyciekiem.

⚠ `Loc.LanguageChanged` wystaje **tylko przy realnej zmianie** — `Loc.Apply` porównuje rozwiązaną kulturę,
więc zapis dowolnej innej preferencji (np. motywu) nie wywoła przebudowy.

### B.6 Zero zmian treści — DOWIEDZIONE

⭐⭐ Przed migracją zrzucono wszystkie wartości **tak, jak wyliczył je KOMPILATOR**, katalog wygenerowano
z tego zrzutu, a po migracji porównano każdą składową z powrotem: **0 rozjazdów na 2 033**. Wyciągnięcie
wartości przez parsowanie źródła dałoby dowód okrężny.

⚠ Pierwszy przebieg dał **22 rozjazdy i wszystkie były błędami NARZĘDZIA, nie produktu**: Python w trybie
tekstowym przepisał `\n`→`\r\n` w 11 stringach, `unicode_escape` zamienił 4 półpauzy w mojibake (dekoduje
jako latin‑1), 4 klucze wypadły z generowania. **Żadnego nie było widać w diffie źródła.**

### B.7 ⚠⚠ Globalny stan `Loc` wymusza serializację testów

Uruchomienie testów mechanizmu **razem** z liveness dało `AboutAuthorFormat` renderujące tekst pustego paska
bocznego. `Loc` to **globalny stan procesu**, sonda liveness podmienia jego katalog, a xunit zrównolegla
KOLEKCJE. W udokumentowanych partycjach to nie zachodziło — było **utajone**.

⭐ Naprawa: `LocalizationMechanismTests` dołączyło do `HeadlessCollection`.
⛔ **Zasada:** test dotykający `Loc.UseCatalogForVerification` musi być w tej samej kolekcji co każdy test
czytający `UiStrings`.

### B.8 Strażniki (32 testy)

Katalog: ładowanie zasobu · kompletność angielskiego · satelita nie wprowadza kluczy · zero zmian wartości ·
żadna składowa nie jest polem. Język: fallback · jedyne źródło to preferencja · brak `language == "pl"` ·
dodanie języka bez zmian w kodzie · żaden widok/VM nie czyta preferencji. Core: brak referencji do
App/Avalonia · `MessageKey` odrzuca prozę · seam działa end-to-end · **strażnik uzbrajający się sam**, gdy
Core zadeklaruje pierwszy klucz. Ratchet: **0 zaszytych w XAML** (zweryfikowany podsadzeniem) · kolumna nie
przypisuje nagłówka · pole nie cache'uje `UiStrings`. Liveness: **binding re-czyta** · property czyta bieżący
język · `LanguageChanged` tylko przy realnej zmianie.

### B.9 Wyniki QA mechanizmu

Na **tymczasowym** katalogu `pl` (2 186 wpisów `[PL] <angielski>`), usuniętym po odbiorze — **7/7**:
zmiana bez restartu · XAML · nagłówki/statusy · kolumny `DataGrid` · wiersz IntelliSense zbudowany PRZED
zmianą · otwarte drzewo bez pozostałości · powrót PL → EN symetryczny.

⭐ Gest klawiszowy **nie** jest tłumaczony — jest komponowany przez `CommandTip`, nigdy nieprzechowywany.

⚠ **Czego QA nie objęło:** kliknięcia w Settings w prawdziwym oknie (brak sterowania UI; modyfikacja
`settings.dat` użytkownika byłaby ryzykiem). Trwały odpowiednik zostaje: `LocalizationLivenessTests`
z podmienialnym katalogiem dwukulturowym w assembly testowym.

### B.10 Świadome wyjątki (pozostają)

| Wyjątek | Powód |
|---|---|
| `ImportRefreshTooltipClipboardNote` — `" (Ctrl+V…)"` | gest w prozie, nie w katalogu; w allowliście **z uzasadnieniem** |
| `C:\data\example.fdb` | przykładowa **ścieżka**, nie zdanie |
| `SettingsCenterViewModel` ×3 — „No such setting in the catalog." | `throw` dla dewelopera, nigdy na ekranie |
| `TraceMonitorTabView` — dopasowanie kolumny po **tekście** nagłówka | ⚠ poprawne tylko dlatego, że obie strony czytają ten sam klucz; ⛔ nie zamieniać żadnej strony na literał |
| `MainWindowViewModel` — numeracja „Query N" | ⚠⚠ **jedyny skutek behawioralny**: zapisane zapytanie zachowuje starą nazwę, więc po zmianie języka numeracja startuje od nowa. Przemianowanie zapytań użytkownika łamałoby regułę #11 |
| **141 pozostałych literałów w App** | kategoria C/D: kompozycje danych, SQL, formaty dat, wzorce plików, definicje siatek, klasy stylów, czcionki, logi deweloperskie |

### B.11 ⛔⛔ 188 wartości z wieloma kluczami — ŚWIADOMIE NIE SCALONE

`"Delete"` ma 12 właścicieli, `"Cancel"` 11, `"Name"` 11. To w większości **różne pojęcia dzielące angielskie
słowo** (czasownik menu vs przycisk potwierdzenia), a język fleksyjny odmieni je różnie. Scalenie odebrałoby
tłumaczowi rozróżnienie kontekstu — **defekt lokalizacyjny udający sprzątanie**.

🔒 **Decyzja użytkownika: w lokalizacji kontekst jest ważniejszy niż mechaniczna deduplikacja.**

⭐ Scalono natomiast tam, gdzie było **kilku właścicieli JEDNEGO pojęcia**: cztery niezależne listy etykiet
rodzajów obiektów → jedno słownictwo `ObjectKind*` (mapowanie zostaje per enum — to cztery różne enumy;
wspólne jest słownictwo, nie `switch`).

### B.12 ⚠⚠ Dodanie języka ma TRZY kroki, nie dwa

1. Wiersz w `PreferenceOptions.Language`.
2. **Etykieta języka** w mapie opcji wiersza `SettingLanguage` w `SettingsCatalog`.
3. Plik `Strings.<kultura>.resx`.

⭐ Krok 2 wykryto **przez faktyczne dodanie `pl`**: padło **36 testów, wszystkie z jednej przyczyny** —
mapa `klucz → etykieta` jest indeksowana wprost, więc język bez etykiety rzuca `KeyNotFoundException` przy
budowaniu strony Settings. Złapał to **istniejący** strażnik `EveryEnumeratedOptionHasALabel`.

---

## C. Co świadomie NIE zostało zrobione

| Pozycja | Rozmiar | Powód |
|---|---|---|
| **Core — migracja producentów** | ≈250 user-visible | 🔒 decyzja użytkownika: bez osobnego przeglądu nie ruszamy publicznych kontraktów **zamkniętych** modułów (Performance, Data Import) — obszar reguły #11 |
| **Firebird — migracja producentów** | ≈46 (`FirebirdConnectionService` 22 + `FirebirdDiagnostics` 24) | jw. |
| **Polskie tłumaczenie** | 2 186 wpisów | ⛔ dopiero po migracji producentów |
| **QA wzrokowe na żywym oknie** | — | ⚠ niewykonalne przy jednym języku: żywy i zamrożony binding renderują ten sam tekst |
| **Sonda renderująca lokalizację** | — | `Loc`/`UiStrings` są `internal`; `InternalsVisibleTo` dla tymczasowego narzędzia to zły kompromis w produkcyjnym `.csproj` |

### C.1 Gdzie dokładnie siedzi ≈280 komunikatów Core/Firebird

| Miejsce | Ile | Co produkuje |
|---|---|---|
| `Core/Performance/**` | ~75 | tytuły, wyjaśnienia, rekomendacje, „What to investigate" doradcy |
| `Core/Settings/ApplicationSettingsStore` + `Settings/Export/**` | ~60 | `LastLoadDiagnostic`, statusy importu ustawień |
| `Firebird/FirebirdDiagnostics` | 24 | komunikaty diagnostyczne |
| `Core/Diagnostics/SessionHealthAnalyzer` | 23 | `Title`, `Impact`, pytania diagnostyczne |
| `Firebird/FirebirdConnectionService` | 22 | `"Could not connect to …"` + komunikat naprowadzający na SRP |
| `Core/Import/**` | ~20 | błędy wierszy importu |
| `Core/Sql/Language/QuickInfo/QuickInfoEngine` | ~12 z 25 | etykiety faktów (`Nullability`, `Default`, `Key`, `Columns`…) |
| `Core/Query/ExecutionSummary` | 9 | `"inserted 8 · updated 16 · …"` |
| `Core/Connections/CharsetCatalog` | 8 | opisy zestawów znaków |
| `Core/Sql/Language/DiagnosticsEngine` | 7 | ET0001–ET0008 |

⚠ **Dowód, że to naprawdę trafia na ekran** (nie założenie): `MainWindowViewModel.cs` wstawia
`health.Diagnostic` z Core wprost do `UiStrings.SettingsUnreadableWarningFormat` i pokazuje w `MessageBanner`.

---

## D. Następny etap — audyt i migracja Core/Firebird

⛔ **Nie zaczynaj od projektowania mechanizmu — mechanizm jest gotowy.** Zacznij od audytu producentów
i przenoszenia ich na seam D‑3.

### D.1 Seam, który już istnieje

```csharp
// EmberTern.Core/Localization/MessageKey.cs
public readonly record struct MessageKey(string Value);       // ⭐ konstruktor ODRZUCA prozę

// EmberTern.Core/Localization/LocalizableMessage.cs
public sealed record LocalizableMessage(MessageKey Key, IReadOnlyList<object?> Arguments)
{
    public static LocalizableMessage Of(MessageKey key, params object?[]? arguments);
    public static LocalizableMessage Of(string key);
}

// EmberTern.App/Localization/Loc.cs
internal static string Format(LocalizableMessage message);     // ⭐ konsument po stronie App
```

⭐ **„Bez prozy w kontrakcie" jest wymuszone KONSTRUKCJĄ, nie testem:** `MessageKey` przyjmuje wyłącznie
token w kształcie identyfikatora (litery, cyfry, `_`, `.`). Zdanie ma spację albo interpunkcję, więc żadne
zdanie nie jest legalnym kluczem.

### D.2 Przepis na migrację jednego producenta

**Krok 1 — Core deklaruje KLUCZ** (nie tekst):

```csharp
// w module, obok typu, który go produkuje
internal static class SessionHealthMessages
{
    public static readonly MessageKey GarbageCollectionBlocked = new("SessionHealth.GcBlocked");
}
```

**Krok 2 — Core zwraca `LocalizableMessage` zamiast `string`:**

```csharp
// przed:  Title = "Garbage collection is blocked",
// po:     Title = LocalizableMessage.Of(SessionHealthMessages.GarbageCollectionBlocked),
```

**Krok 3 — App dodaje WPIS do `Strings.resx`** pod dokładnie tym kluczem, z **dokładnie dotychczasową**
angielską wartością.

**Krok 4 — App rozwiązuje przy prezentacji:** `Loc.Format(message)` w ViewModelu, który to pokazuje.

⭐ **Właściciel klucza to Core, właściciel słów to App.** Wiąże je strażnik
`EveryCoreMessageKey_HasAnEnglishEntry` — **uzbraja się sam** przy pierwszym zadeklarowanym kluczu.

### D.3 Kolejność, którą sugeruję (do potwierdzenia przez użytkownika)

1. **Audyt jak dla App** — cztery niezależne metody, bo jedna ma ślepe plamy (zmierzone: skan „prozy"
   przeoczył pojedyncze słowa PascalCase, skan kształtu użycia przeoczył krótkie literały).
2. **Klasyfikacja B/C/D/E** — ⚠ szczególnie **E**: surowy komunikat **serwera** ma zostać surowy; naszym
   zdaniem jest tylko opakowanie. `MapErrorMessage` ma to już zapisane w komentarzu.
3. **Zacznij od modułów, które NIE są zamknięte** — `SessionHealthAnalyzer`, `DiagnosticsEngine`,
   `QuickInfoEngine`, `ExecutionSummary`, `CharsetCatalog`. ⛔ Performance i Data Import **dopiero po
   osobnej decyzji** (publiczne kontrakty zamkniętych modułów).
4. **Dopiero potem** tłumaczenie na polski.

### D.4 Pułapka, na którą uważaj

⚠ `Finding.Title`, `Finding.Explanation`, `Recommendation` itd. to `string` na **rekordach publicznych**.
Zmiana na `LocalizableMessage` to zmiana kontraktu — i to jest dokładnie powód, dla którego użytkownik
odłożył ten moduł. **Zmierz zasięg PRZED zaproponowaniem zmiany**, nie po.

---

## E. Zasady dla nowej sesji

1. ⛔ **Nie wracaj do zamkniętego App/XAML bez konkretnego, zgłoszonego defektu.**
2. ⛔ **Nie przebudowuj mechanizmu `Loc`** — w szczególności nie wracaj do indeksera (§B.3).
3. ⛔ **Nie scalaj kluczy mechanicznie** tylko dlatego, że mają identyczny angielski tekst (§B.11).
4. ⛔ **Nie dodawaj `if (language == "pl")`** ani żadnego odpowiednika — pilnuje tego strażnik.
5. ⛔ **Nie przenoś lokalizacji do Core przez referencję do `UiStrings`** — łamie regułę architektury #1;
   od tego jest seam D‑3.
6. ⛔ **Nie zaczynaj tłumaczenia na polski** przed zakończeniem migracji producentów user-visible
   w Core/Firebird.
7. ⚠ **Nie usuwaj `LocalizationMechanismTests` / `LocalizationLivenessTests` z filtra partycji headless**
   (§B.7).
8. ⚠ **Zachowuj dokładnie obecne wartości angielskie** przy każdej migracji — i dowodź tego porównaniem
   przed/po, nie deklaracją (§B.6).

---

## DECYZJE OCZEKUJĄCE NA UŻYTKOWNIKA

⚠ Wyłącznie rzeczy **nierozstrzygnięte**. Wszystko inne w tym dokumencie jest już zdecydowane.

1. **Czy `feat/localization` ma zostać scalona do `master`?** Nie ma wcześniejszej jednoznacznej decyzji
   o takim merge'u dla tej gałęzi, więc **nie scalono**. ⚠ Kontekst: `feat/product-polish` została scalona
   dopiero na wyraźne polecenie, po zamknięciu całego etapu.
2. **Czy migracja Core/Firebird obejmuje moduły ZAMKNIĘTE** (Performance ~75, Data Import ~20)? To zmiana
   publicznych kontraktów; Data Import ma stojącą dyrektywę „wracać tylko po rzeczywisty defekt funkcjonalny".
3. **Jak daleko idzie granica „surowy komunikat serwera"?** Czy opakowania w `FirebirdConnectionService`
   (w tym komunikat naprowadzający na SRP) mają być lokalizowalne w całości, czy tylko ich część nie-serwerowa?
4. **Czy chcesz wizualne QA na żywym oknie przed migracją Core?** Wymaga tymczasowego katalogu `pl`
   + etykiety języka (§B.12) i **Twojego kliknięcia** w Settings. Odtworzenie: jeden przebieg generatora.
5. **Czy `LocalizationLiveQaTests` ma wrócić na stałe** (w wersji na podmienialnym katalogu), czy zostaje
   przy jednorazowym QA? Obecnie usunięty; trwały odpowiednik to `LocalizationLivenessTests`.
