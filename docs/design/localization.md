# Localization — architecture and as-built

**Status: 🔒 ETAP LOCALIZATION / APP — ZAMKNIĘTY I PRZYGOTOWANY DO ODBIORU (2026-08-09).**
Mechanizm zbudowany, warstwa App zmigrowana w całości, dług lokalizacyjny App wyzerowany.
⛔ **Żaden tekst nie jest przetłumaczony** — angielski jest jedynym językiem i jednocześnie bazowym.
⛔ **Core/Firebird świadomie POZA zakresem** (≈280 komunikatów user-visible) — osobny etap, patrz §7.
Punkt wyjścia etapu: [localization-readiness-audit.md](localization-readiness-audit.md).

---

## 1. Ratyfikowane decyzje

| # | Decyzja | Treść |
|---|---|---|
| **D‑1** | **Moment zmiany języka** | ⭐ **NA ŻYWO.** Zmiana `Language` w Settings przemalowuje działającą aplikację; restart nie jest potrzebny. |
| **D‑2** | **Nośnik** | **`.resx` + `ResourceManager`**, angielski jako zestaw neutralny (bazowy), kolejne języki jako satelity. |
| **D‑3** | **Core / Firebird** | Core i Firebird oddają **`MessageKey` + argumenty**; słowa rozwiązuje warstwa App. Surowe komunikaty *serwera* mogą zostać surowe; **nasze** opakowania — nie. |

⚠⚠ **D‑1 zostało odwrócone w trakcie prac** i to jest zapis historyczny, nie ciekawostka. Pierwotnie
ratyfikowano „po restarcie", zbudowano pod to mechanizm (`static readonly` + bootstrap w `Program.Main`),
po czym decyzja zmieniła się na „na żywo". Skutek dla architektury był **jakościowy, nie ilościowy**:
`{x:Static}` i `static readonly` przestały wystarczać, bo żadne z nich nie re-ewaluuje.

⭐ Wersja żywa jest przy tym **prostsza**, nie trudniejsza: wariant restartowy musiał ustalać język
w `Program.Main`, **przed startem Avalonii**, bo `static readonly` rozwiązuje się przy pierwszym dotknięciu
i jeden wczesny odczyt zamroziłby sesję po angielsku — cicho, przy zielonym buildzie. Odczyt na żywo tej
kolejności nie ma; język wpina się tam, gdzie motyw.

### ⛔ Uchylenie reguły architektury #6

CLAUDE.md, reguła #6, brzmiała: *„No `AppResources.resx`. Use `UiStrings`"*. **Uchylona świadomie dla
lokalizacji (D‑2)** — i uchylona jest tylko jej połowa dotycząca NOŚNIKA. Reszta reguły stoi:
`UiStrings` pozostaje **jedynym** miejscem, przez które kod C# sięga po tekst; zmieniło się to, skąd
`UiStrings` bierze wartość. ⛔ Nie wolno czytać `ResourceManager` bezpośrednio z ViewModelu ani z widoku.

---

## 2. Mechanizm — cztery elementy

```
Preferences.Language ──► LanguagePreference.CultureFor ──► Loc (kultura + katalog)
                                                            │
                            ┌───────────────────────────────┼───────────────────────────┐
                            ▼                               ▼                           ▼
                     UiStrings.X (property)          LocalizationSource            Loc.LanguageChanged
                     — dla kodu C#                   — dla XAML: {app:Loc X}       — dla tych, którzy
                                                                                     zapamiętali tekst
```

| Plik | Rola |
|---|---|
| `src/EmberTern.App/Localization/Strings.resx` | **2 186 wpisów**, angielski bazowy. Klucz = nazwa składowej `UiStrings`. |
| `src/EmberTern.App/Localization/Loc.cs` | Jedyny resolver. `Text(key)` rozwiązuje **w chwili wywołania**. |
| `src/EmberTern.App/Localization/LocalizationSource.cs` | Jeden mały obiekt powiadamiający **na klucz**; `{app:Loc}` binduje jego `Value`. |
| `src/EmberTern.App/Localization/LanguagePreference.cs` | Klucz preferencji → `CultureInfo`. Odpowiednik `ThemePreference`. |
| `src/EmberTern.App/LocMarkup.cs` | `{app:Loc Key}` — zwraca `Binding`, nie string. |
| `src/EmberTern.Core/Localization/MessageKey.cs` | Klucz komunikatu Core. **Odrzuca prozę konstruktorem.** |
| `src/EmberTern.Core/Localization/LocalizableMessage.cs` | Klucz + argumenty. Seam D‑3. |

### 2.1 ⚠⚠ Dlaczego NIE indekser — znalezisko pomiarowe

Pierwsza wersja `LocalizationSource` była tym, co robi każda biblioteka lokalizacyjna: **jeden obiekt
z indekserem `this[key]`**, powiadamiany raz. Test headless na prawdziwym `TextBlock` orzekł, że **to nie
działa**: wartość początkowa bindowała się poprawnie, a po zmianie języka kontrolka **pokazywała stary
tekst**. Ani konwencja WPF `"Item[]"`, ani `string.Empty` („wszystko się zmieniło") nie docierają do
bindingu po indekserze w Avalonii 12.1.1.

⭐ Dlatego klucz jest bindowany przez **zwykłą właściwość** (`LocalizedString.Value`) na małym obiekcie per
klucz, powiadamianą po nazwie. Koszt: ~940 obiektów po kilkadziesiąt bajtów. ⛔ **Nie „upraszczać" tego
z powrotem do indeksera** — wersja z indekserem renderuje się poprawnie przy pierwszym załadowaniu, co
czyni awarię trudną do zauważenia.

### 2.2 Trzy formy składowej i dlaczego tylko jedna jest dopuszczalna

| Forma | Kiedy się rozwiązuje | Werdykt |
|---|---|---|
| `const` | inline'owana przez kompilator — po buildzie **nie ma czego rozwiązywać** | ⛔ |
| `static readonly` | **raz**, przy inicjalizacji typu | ⛔ renderuje poprawnie i zamarza w pierwszym języku |
| `static string X => Loc.Text(nameof(X))` | przy każdym odczycie | ✅ |

⚠ Analogicznie w XAML: `{x:Static}` **nie jest bindingiem** i nigdy nie re-ewaluuje. Obowiązuje `{app:Loc}`.

⚠ **Koszt zamiany, powiedziany wprost: straciliśmy sprawdzanie klucza przez kompilator.** `{x:Static}` był
weryfikowany przy budowaniu, `{app:Loc Key}` niesie klucz jako string. Rekompensuje to strażnik
`EveryLocKeyInXaml_ExistsInTheCatalog`; ⛔ jego usunięcie zamienia świadomy kompromis w regresję.

### 2.3 Granica, której binding nie przekracza

Tekst **zapamiętany raz** w C# (nagłówek zakładki nadany przy otwarciu, kolumna siatki budowana
w code-behind, wiersz IntelliSense) nie odświeży się sam — nikt go ponownie nie czyta. Dla nich jest
`Loc.LanguageChanged`. ⚠ Zdarzenie wystaje **tylko przy realnej zmianie**: `Loc.Apply` porównuje
rozwiązaną kulturę, więc zapis dowolnej innej preferencji (np. motywu) nie wywoła przebudowy.

⭐ **Wpięte powierzchnie:** `MainWindowViewModel` (+ każda otwarta zakładka przez `RaiseAllPropertiesChanged`)
— to pokrywa cały tekst, który VM wylicza raz i publikuje.

⭐⭐ **Dwie klasy konsumentów zostały jednak rozwiązane LEPIEJ niż zdarzeniem — przez usunięcie cache'u:**
kolumny `DataGrid` budowane w kodzie **bindują** `HeaderProperty` (`LocalizedColumn.Header`, bo
`DataGridColumn.Header` jest `StyledProperty`), a wiersz IntelliSense przestał zapamiętywać opis rodzaju
i rozwiązuje go we właściwości. ⭐ Binding jest lepszy od subskrypcji: nie ma czego wyrejestrować, nie ma
kolejności i nie da się zapomnieć — a subskrypcja per wiersz listy uzupełniania byłaby wyciekiem.
⚠ Pilnują tego dwa strażniki: `NoCodeBuiltColumn_AssignsALocalizedHeader` i `NoField_CapturesALocalizedString`.

---

## 3. Jak dodać kolejny język

1. Wiersz w `PreferenceOptions.Language` (np. `"pl"`).
2. **Etykieta języka** w mapie opcji wiersza `SettingLanguage` w `SettingsCatalog` (+ jej klucz w katalogu).
3. Plik `src/EmberTern.App/Localization/Strings.<kultura>.resx` z przetłumaczonymi wartościami.

⚠⚠ **KROK 2 ZOSTAŁ DOPISANY PO QA I KORYGUJE WCZEŚNIEJSZY ZAPIS TEGO ROZDZIAŁU.** Dokument twierdził
„wiersz + plik `.resx`, i to wszystko". Zmierzone przez faktyczne dodanie `pl`: **36 testów padło, wszystkie
z jednej przyczyny** — mapa opcji tego wiersza jest słownikiem `klucz → etykieta`, a `PreferenceSettingViewModel`
indeksuje go wprost, więc język bez etykiety rzuca `KeyNotFoundException` przy budowaniu strony Settings.
⭐ Złapał to **istniejący** strażnik `EveryEnumeratedOptionHasALabel` — mechanizm zadziałał, nieprawdziwy był
mój opis. ⭐ Krok jest przy tym nieusuwalny z natury rzeczy: nazwa języka jest tekstem i ktoś musi ją podać.

⭐ **Poza tym rzeczywiście nic więcej.** Zero zmian w widokach, ViewModelach, konwerterach; zero rozgałęzień.
Klucz nieprzełożony spada na angielski automatycznie (fallback `ResourceManager`). Strażnik
`EveryLanguageInTheCatalog_ResolvesToItsOwnCultureWithNoCodeChange` chodzi po katalogu, więc **rozszerza się
sam** przy dodaniu wiersza — co QA potwierdziło: przy `pl` w katalogu przeszedł bez zmian.

---

## 4. Seam D‑3 (Core / Firebird)

Core oddaje `LocalizableMessage(MessageKey, args)`; App rozwiązuje przez `Loc.Format`.

⭐ **„Bez prozy w kontrakcie" jest wymuszone KONSTRUKCJĄ, nie testem:** `MessageKey` przyjmuje wyłącznie
token w kształcie identyfikatora (litery, cyfry, `_`, `.`). Zdanie ma spację albo interpunkcję, więc żadne
zdanie nie jest legalnym kluczem. Strażnik sprawdza już tylko, czy konstruktor nadal odmawia.

⚠ **Argumenty to DANE i mogą zawierać angielski** — nazwa tabeli, ścieżka, surowy komunikat Firebirda. To jest
zamierzone i jest właśnie sposobem, w jaki granica z D‑3 jest utrzymywana: nasze zdanie jest kluczem,
wypowiedź serwera jest argumentem.

⏸ **Seam ma konsumenta (`Loc.Format`), ale nie ma jeszcze PRODUCENTA w Core** — świadomy wyjątek od zasady
„żadnego komponentu bez konsumenta" (#233), zgłoszony wprost w komentarzu typu. ⛔ Nie usuwać jako martwego
kodu; ~250–300 miejsc w Core czeka na osobny etap.

---

## 5. Co zostało zrobione (as-built)

| Pozycja | Liczba |
|---|---|
| Wpisy w katalogu angielskim | **2 186** |
| Składowe `UiStrings` (wszystkie property, zero pól) | **2 186** |
| Miejsca w XAML `{app:Loc}` | **1 259** |
| **Zaszyte teksty user-visible w XAML** | ⭐ **0** |
| **Zaszyte teksty user-visible w App C#** | ⭐ **0** |
| Zmienione wartości angielskie | ⭐ **0** |

⭐⭐ **Zerowa zmiana wartości przy migracji `UiStrings` jest DOWIEDZIONA**: przed migracją zrzucono wszystkie
wartości **tak, jak wyliczył je KOMPILATOR**, katalog wygenerowano z tego zrzutu, a po migracji porównano
każdą składową z powrotem. Parsowanie źródła dałoby dowód okrężny.

⚠ **Pierwszy przebieg dał 22 rozjazdy — wszystkie były błędami NARZĘDZIA:** Python w trybie tekstowym
przepisał `
` na `

` w 11 stringach, `unicode_escape` zamienił 4 półpauzy w mojibake (dekoduje jako
latin‑1), 4 klucze wypadły z generowania. Żadnego nie było widać w diffie źródła.

### 5.1 Deduplikacja — co scalono i czego świadomie NIE

⭐ **Scalone (kilku właścicieli JEDNEGO pojęcia):** cztery niezależne listy etykiet rodzajów obiektów
(`QuickInfoView.KindLabel` · `SqlCompletionData.DescribeKind` · `MetadataNodeViewModel.KindNounTitle` ·
`NavigationController.KindLabel`) czytają jedno słownictwo `ObjectKind*`. Mapowanie zostaje per enum — to
CZTERY różne enumy — wspólne jest słownictwo, nie `switch`. Dodatkowo: tooltipy Continue/Restart komponują
się z etykiety przycisku, a zaszyte `"New folder"` okazało się duplikatem istniejącego `FolderDefaultName`.

⛔⛔ **NIE scalono 188 wartości mających po kilka kluczy — decyzja ratyfikowana przez użytkownika.**
`"Delete"` ma 12 właścicieli, `"Cancel"` 11, `"Name"` 11. To w większości **różne pojęcia dzielące angielskie
słowo** (czasownik menu vs przycisk potwierdzenia), a język fleksyjny odmieni je różnie. Scalenie byłoby
defektem lokalizacyjnym udającym sprzątanie: odbiera tłumaczowi rozróżnienie kontekstu. ⭐ Zasada:
**w lokalizacji kontekst jest ważniejszy niż mechaniczna deduplikacja.**

### 5.2 Znaleziska

⚠ **`TraceMonitorTabView.axaml.cs` — jedyne użycie `UiStrings` w pozycji wymagającej `const`** (ramiona
`switch`). ⚠ Dopasowuje kolumnę po **tekście nagłówka**; działa pod tłumaczeniem tylko dlatego, że obie
strony czytają ten sam klucz. ⛔ Nie zamieniać żadnej ze stron na literał.

⚠ **`MainWindowViewModel` — numeracja „Query N".** Prefiks nie może być `const`, i to jedyne miejsce,
gdzie ma to skutek BEHAWIORALNY: zapisane zapytanie zachowuje starą nazwę, więc po zmianie języka numeracja
startuje od nowa. ⭐ Przyjęte świadomie — przemianowanie zapytań użytkownika za jego plecami łamałoby regułę #11.

⭐ **Przeniesienie strażnika gestów na wartości zasobów natychmiast znalazło niezapisany wyjątek**
(`ImportRefreshTooltipClipboardNote`), strukturalnie niewidoczny dla starej wersji czytającej `const`y.

## 5.3 Wyniki QA mechanizmu

QA wykonano na **tymczasowym** katalogu `pl` (2 186 wpisów `[PL] <angielski>`), usuniętym po odbiorze.
⭐ Mechanicznie, nie tłumaczenie: powierzchnia, która się NIE odświeżyła, była widoczna od razu — wiarygodna
polszczyzna właśnie to by ukryła.

| Sprawdzane | Wynik |
|---|---|
| EN → PL bez restartu | ✅ jedyne wywołanie to `Loc.Apply("pl")` — to samo, co robi radio w Settings |
| Teksty XAML | ✅ |
| Nagłówki / statusy / podsumowania | ✅ + ⭐ gest klawiszowy **nie** jest tłumaczony (komponowany, nigdy nieprzechowywany) |
| Kolumny `DataGrid` budowane w kodzie | ✅ |
| Wiersz IntelliSense zbudowany PRZED zmianą | ✅ |
| Otwarte drzewo kontrolek bez starego tekstu | ✅ skan `GetLogicalDescendants`, zero pozostałości |
| Powrót PL → EN | ✅ symetryczny, wartość identyczna z wyjściową |

⚠ **Czego QA NIE objęło:** kliknięcia w Settings w prawdziwym oknie — sterowanie UI aplikacji nie było
dostępne, a modyfikacja `settings.dat` użytkownika byłaby ryzykiem. ⛔ Sondy renderującej nie dostarczono:
`Loc`/`UiStrings` są `internal`, a `InternalsVisibleTo` dla tymczasowego narzędzia to zły kompromis
w produkcyjnym `.csproj`. Dowodem jest 7 asercji na **zrealizowanych kontrolkach**.

⭐ **Trwały odpowiednik QA zostaje w repo:** `LocalizationLivenessTests` mierzy to samo przez podmienialny
katalog dwukulturowy zdefiniowany w assembly testowym — więc liveness jest mierzalny również bez `pl`.

## 5.4 ⚠⚠ Znalezisko: globalny stan `Loc` wymusza serializację testów

Uruchomienie testów mechanizmu **razem** z liveness dało `AboutAuthorFormat` renderujące tekst pustego
paska bocznego. Przyczyna: `Loc` jest **globalnym stanem procesu**, sonda liveness podmienia jego katalog,
a xunit zrównolegla KOLEKCJE. W udokumentowanych partycjach to nie zachodziło — było **utajone**.

⭐ Naprawa u źródła: `LocalizationMechanismTests` dołączyło do `HeadlessCollection`, co serializuje je
z liveness. ⚠ Koszt: **dwie nazwy więcej w kruchym filtrze partycji headless**; partycja główna **8 280**,
zgrupowana **164**, izolowana **55**.

⛔ **Zasada na przyszłość:** test dotykający `Loc.UseCatalogForVerification` musi być w tej samej kolekcji
co każdy test czytający `UiStrings`. Inaczej wraca wyścig — cichy, rzadki i mylący.

## 6. Strażniki

| Strażnik | Czego pilnuje |
|---|---|
| `TheEnglishResourceSet_Loads` | zasób w ogóle się ładuje (nazwa manifestu to string — literówka jest cicha aż do pierwszego odczytu) |
| `EnglishBase_ResolvesEveryKeyItDeclares` | angielski jest kompletny |
| `NoShippedCulture_IntroducesAKeyEnglishLacks` | tłumaczenie tłumaczy klucze, nigdy ich nie wprowadza (uzbraja się samo przy pierwszym satelicie) |
| `EveryLocalizedMember_MatchesItsEnglishEntry` | zero zmian tekstu; wpis bez składowej to sierota |
| `NoLocalizedMember_IsInlinedByTheCompiler` | żadna składowa nie jest polem (`const` ani `static readonly`) |
| `AnUnusableLanguage_FallsBackToEnglish` | pusty / nieznany język → angielski |
| `TheLanguage_ComesOnlyFromThePreference` | brak `CurrentUICulture`, zmiennych środowiskowych, drugiego źródła |
| `NoCode_BranchesOnAParticularLanguage` | brak `language == "pl"` |
| `EveryLanguageInTheCatalog_ResolvesToItsOwnCultureWithNoCodeChange` | dodanie języka nie wymaga kodu |
| `NoViewOrViewModel_ReadsTheLanguagePreference` | język dociera do UI wyłącznie jako gotowy tekst |
| `Core_ReferencesNeitherAppNorAvalonia` | reguła #1 nienaruszona |
| `AMessageKey_RefusesProse` / `_AcceptsAnIdentifier` | kontrakt D‑3 |
| `ACoreMessage_ResolvesToEnglishTextInTheAppLayer` | seam działa end-to-end |
| `EveryCoreMessageKey_HasAnEnglishEntry` | uzbraja się sam, gdy Core zadeklaruje pierwszy klucz |
| ⭐ `ABoundString_RereadsWhenTheLanguageChanges` | **pomiar, na którym stoi cała decyzja D‑1** |
| `AUiStringsMember_ReadsTheCurrentLanguage` | odczyt z C# też jest żywy |
| `LanguageChanged_FiresForCaptureOnceConsumers` | i tylko przy realnej zmianie |

⚠ Trzy testy liveness używają **podmienialnego katalogu** (`Loc.UseCatalogForVerification`) z dwoma
kulturami zdefiniowanymi w assembly TESTOWYM. Bez tego twierdzenie „binding re-czyta" jest niemierzalne przy
jednym języku: żywy i zamrożony binding renderują identyczny tekst. ⛔ Seam nie jest wołany z produktu.

---

## 7. Co zostaje otwarte

| Pozycja | Rozmiar | Dlaczego |
|---|---|---|
| **Core / Firebird** | ≈280 user-visible | ⛔ **Odłożone decyzją użytkownika.** Seam D‑3 gotowy i przetestowany end-to-end, ale migracja zmienia publiczne kontrakty ZAMKNIĘTYCH modułów (Performance, Data Import) — obszar reguły #11. Osobny krok po przeglądzie. |
| **Polskie tłumaczenie** | 2 186 wpisów | świadomie po uporządkowaniu tekstów |
| **QA wzrokowe na żywym oknie** | — | ⚠ niewykonalne przed tłumaczeniem: przy jednym języku żywy i zamrożony binding renderują ten sam tekst |
