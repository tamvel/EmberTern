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

✅ **Seam ma PRODUCENTA od etapu C1** (`SessionHealthMessages` — 16 kluczy; narracja:
[../history/28-localization-core-stage.md](../history/28-localization-core-stage.md)). Wcześniejszy zapis
„brak producenta, świadomy wyjątek od #233" jest historyczny.

### 4.1 ⚠ Katalog ma DWÓCH właścicieli — i strażnik jest przez to PODZIELONY, nie osłabiony

Od C1 wpis w `Strings.resx` należy albo do App (nazwa = property `UiStrings`), albo do Core (klucz
`MessageKey`, rozwiązywany przez `Loc.Format`). ⛔ Klucz Core **nie może** mieć składowej o swojej nazwie —
zawiera kropki, więc nie jest legalnym identyfikatorem C#.

Dyskryminatorem jest **refleksja po zadeklarowanych polach `MessageKey`** (w assembly Core **i** Firebird),
nigdy konwencja kropki — konwencja byłaby drugim źródłem prawdy o tym, kto jest właścicielem klucza. Obie
partycje zachowują ochronę przed sierotami **w obu kierunkach**:

| Partycja | Asercja |
|---|---|
| App | musi mieć property `UiStrings` (`EveryLocalizedMember_MatchesItsEnglishEntry`) |
| Core | musi mieć wpis angielski (`EveryCoreMessageKey_HasAnEnglishEntry`) **+** wpis, którego nikt nie deklaruje, jest sierotą (`EveryCoreShapedEntry_IsDeclaredByCore`) |

⭐ Podsadzenie przemianowanego klucza zapala **trzy** strażniki: źle napisany klucz Core wpada do partycji App
i tam też jest łapany — **między połówkami nie ma szczeliny**.

### 4.0 ⭐⭐ `LocalizableMessage` ma RÓWNOŚĆ STRUKTURALNĄ (od C5) — i to jest warunek, nie ozdoba

Równość generowana przez rekord porównywała `IReadOnlyList<object?>` **po referencji**, więc dwa komunikaty
o tym samym kluczu i tych samych danych były nierówne. Nieszkodliwe przez cztery etapy (nikt ich nie porównywał)
i **defekt w chwili, gdy jeden z nich trafia do typu wartościowego**: `Diagnostic` to `readonly record struct`,
a `DiagnosticsPanelViewModel.Update` używa jego równości, żeby **nie** przebudowywać kolekcji i **nie** gubić
zaznaczenia użytkownika.

⭐ Naprawione u **nośnika**, nie u konsumenta: `Equals`/`GetHashCode` porównują klucz i argumenty element po
elemencie. Jedna zmiana obsługuje każdego przyszłego osadzającego; wariant „nośnik o stałej arności w `Diagnostic`"
odrzucony (sufit arności to defekt zaplanowany na później).

⚠ **Przesłanka, którą to wnosi i którą PILNUJEMY:** argument musi sam być wartościowo porównywalny (`string`,
liczba — `int` zaboksowany porównuje się wartościowo). ⛔ `byte[]`/`char[]` po cichu przywraca porównanie
referencji. Strażnik: `DiagnosticsLocalizationTests.NoProducerPassesAnArgumentWithoutValueEquality`.
Gotcha **#358**.

### 4.2 🔒 Liczby idą za kulturą CZYTELNIKA — zachowanie OCZEKIWANE, nie regresja

`Loc.Format` formatuje argumenty pod `CurrentCulture`, podczas gdy słowa wybiera `Loc.Culture`. To jest
ratyfikowana konwencja aplikacji (ta sama, co `DateTimeDisplay` i ~30 istniejących wywołań
`string.Format(CultureInfo.CurrentCulture, …)`).

⚠ **Skutek przy migracji producenta, który wcześniej formatował invariantnie:** licznik w `SessionHealth`
renderuje się jako **`48 102`** na maszynie polskiej tam, gdzie wcześniej było `48,102` — przy **zerowej**
zmianie znaków w kodzie i w wartości zasobu.

🔒 **Ratyfikowane przez użytkownika (2026-08-10) jako zachowanie oczekiwane.** ⛔ Nie „naprawiać" z powrotem do
grupowania invariantnego i nie zgłaszać jako regresji. ⚠ Każdy kolejny migrowany producent dziedziczy tę samą
zmianę — gotcha **#354** opisuje ogólny kształt (przeniesienie miejsca wywołania na wspólny formater
przenosi też decyzję o kulturze).

⚠ **Konsekwencja dla testów:** asercja na **argument** (`Arguments[0] == 48102L`) jest mocniejsza i przenośna;
asercja na `"48,102"` jest przypięta do maszyny, na której powstała.

### 4.3 ⭐⭐ Liczba mnoga — RODZINA KLUCZY + nazwany ZESTAW REGUŁ (etap C6)

Klucz niosący liczbę może rozwiązywać się do **rodziny wariantów** o sufiksach kategorii CLDR:

```
Strings.resx        Localization.PluralRuleSet = one-other
                    Query.Exec.RowsInserted.one   = {0} row inserted
                    Query.Exec.RowsInserted.other = {0} rows inserted
Strings.pl.resx     Localization.PluralRuleSet = one-few-many
                    Query.Exec.RowsInserted.one / .few / .many
```

⭐⭐ **O tym, czy zdanie POTRZEBUJE form liczby, decyduje JĘZYK, nie producent.** `Loc.Format` sonduje warianty
**w katalogu renderowanej kultury**, więc angielski może trzymać wpis płaski tam, gdzie polski deklaruje trzy
warianty tego samego klucza, i żadna ze stron nie musi wiedzieć, co zrobiła druga. ⛔ Flaga „to jest komunikat
liczbowy" na `LocalizableMessage` byłaby twierdzeniem Core o gramatyce, której Core nie zna — i zamroziłaby
angielski podział dwuelementowy w kontrakcie.

⛔ **Zestaw reguł nazywa GRAMATYKĘ, nigdy języka** (`one-other`, `one-few-many`): kilka języków dzieli jeden
kształt (francuski i hiszpański to `one-other`, rosyjski i czeski to `one-few-many`), więc nazwa językowa
byłaby fałszywa przy drugim konsumencie — i odtwarzałaby rozgałęzienie per-język, którego zakazuje
`NoCode_BranchesOnAParticularLanguage`, o warstwę dalej, niż ten strażnik sięga. Pilnuje tego
`NoRuleSet_IsNamedAfterALanguage`.

⚠ **Co dokładnie znaczy „nowy język nie wymaga kodu" — powiedziane ściśle, nie hojnie.** Język o gramatyce już
zamodelowanej nie wymaga nic poza wierszem w katalogu i plikiem `.resx`. Język o gramatyce genuinnie nowej
(arabski — sześć kategorii, irlandzki — pięć) wymaga nowego zestawu reguł, czyli **kodu**. To jest uczciwe:
nowy ALGORYTM nie jest tłumaczeniem. ⛔ Alternatywa — reguła jako WYRAŻENIE parsowane z zasobu w runtime —
została rozważona i **odrzucona**: repozytorium zapłaciło już raz za mini-język ewaluowany na ścieżce, która
nie ma prawa rzucić (`TreeDiagnostics` — narzędzie napisane, żeby aplikacja nie padła, stało się tym, co ją
zabiło).

⚠ **Liczba jest ZAWSZE argumentem {0}** (ratyfikowane R3), czytanym w jednym miejscu —
`LocalizableMessage.TryGetCount`. To METODA, nie składowa, więc równość strukturalna z §4.0 jest nietknięta.
Dwóch czytelników pytających „gdzie jest liczba" na dwa sposoby to sposób, w jaki rozjeżdża się forma dualna
(#357).

⚠ Trzy poziomy degradacji i żaden nie rzuca: dokładna kategoria → `other` (własny catch-all CLDR) → klucz
płaski. Odpowiedzią build-time jest `EveryPluralFamily_IsCompleteInEveryShippedCulture`; to jest odpowiedź
runtime, z tego samego powodu, dla którego `Loc.Text` zwraca klucz zamiast rzucać.

⭐ **`Loc.FormatParts`** rozcina rozwiązane zdanie wokół jego liczby, żeby powierzchnia mogła wyróżnić DANĄ,
nie wiedząc, gdzie w zdaniu ta dana stoi. ⛔ Powstało z konkretnego defektu: karta aktywności bindowała
`Count` i `Verb` obok siebie, czyli miała **angielski szyk zapisany w UKŁADZIE**.

### 4.2a ⚠⚠ Mechanizm §4.2 to SPECYFIKATOR w wartości zasobu, nie kultura gołego podstawienia (zmierzone w C4b)

⭐ **Zmierzone przez podsadzenie, i koryguje intuicję:** `string.Format(pl-PL, "{0}", 2000000000)` **NIE stawia
separatorów grup**. Kultura rządzi separatorem dziesiętnym i znakiem minus, nie grupowaniem liczby całkowitej
w formacie `G`. Rozjazd `48,102` → `48 102` z §4.2 pochodzi z **`{0:N0}`** w wartości zasobu
(`SessionHealth.Evidence.Gap` = `OAT lag {0:N0} · OST {1:N0} · Next {2:N0}`).

⚠ **To przenosi zagrożenie z MASZYNY na TŁUMACZA** — i dotyczy wyłącznie wzorca dualnego (angielski literał
u producenta + wpis zasobu obok). Tłumacz, który dopisze `:N0` do dziewięciocyfrowej liczby, rozjedzie obie
połówki i zapali strażnik równości z powodu niemającego nic wspólnego ze zdaniem.

🔒 **Reguła z C4b:** liczba, która jest **ECHEM pola technicznego** (zadeklarowana wersja formatu, licznik
iteracji KDF), jedzie jako **string sformatowany invariantnie** — specyfikator jest wtedy bezczynny, a obie
połówki są identyczne z konstrukcji. ⛔ Liczba, którą czytelnik **liczy** (sesje, wiersze), nadal idzie za jego
kulturą — §4.2 stoi. Gotcha **#357**.

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
| **Core / Firebird** | ⚠ **~170–190**, nie 280 | 🚧 **W TRAKCIE — etap Core/Firebird; po C4b zadeklarowanych kluczy Core jest 76.** ⭐ Liczba 280 **nie przetrwała pomiaru** (C0): `CharsetCatalog` 8→**0**, Data Import ~20→**0** (już poprawny, enum), `FirebirdDiagnostics` 24→**0** (klasa E, log deweloperski). Kolejność i klasyfikacja: [../history/28-localization-core-stage.md](../history/28-localization-core-stage.md) |
| ↳ ✅ `SessionHealthAnalyzer` | 20 z 22 | **C1 ZROBIONE** i odebrane |
| ↳ ✅ `QuickInfoEngine` | 18 etykiet | **C2 ZROBIONE** i odebrane. ⭐ Migracja to **PODZIAŁ**: etykieta jest nasza (klucz), **wartość zostaje dosłowna** (słownictwo Firebirda — `NOT NULL`, `PRIMARY KEY`, `BEFORE INSERT`) |
| ↳ ✅ `ApplicationSettingsStore` | 18 kluczy | **C4a ZROBIONE**. ⭐ Wzorzec dualny z C3 (angielski + bliźniak lokalizowalny), bo ~20 istniejących testów przypina DOKŁADNE brzmienie odmowy na powierzchni reguły #11 — dzięki temu dowód zerowej zmiany treści to **nietknięte** testy |
| ↳ ✅ **`Settings/Export`** | **20 kluczy** | **C4b ZROBIONE** — tym samym całe C4 domknięte. ⭐ Rodzina `Damaged*` to **CZTERY CAŁE ZDANIA**, nie prefiks + fragment; angielska połowa nadal SKŁADANA, żeby strażnik równości był dowodem, a nie porównaniem przepisanego zdania. ⭐ Dwie odmowy store'a **przewleczone**, nie powtórzone (`CanSave(out,out)` + `LastSaveMessage` z C4a). ⛔ Strona EKSPORTU świadomie poza zakresem — jej dwa `ArgumentException` są nieosiągalne za bramką `CanExport`. ⚠ Jeden klucz (`NoMigrationStep`) jest dziś **nieosiągalny** i ma nazwany wyjątek z **przypiętą przesłanką**. Znalezisko: §4.2a + gotcha #357 |
| ↳ ✅ `FirebirdConnectionService` | 4 klucze | **C3 ZROBIONE** i odebrane. ⭐ Wzorcowy D‑3: nasze zdanie = klucz, **surowy komunikat serwera = argument**. ⛔ `Legacy_Auth` rozpoznawany po tekście SERWERA, przypięte pod zmianą języka |
| ↳ ✅ **`ExecutionSummary` / `ExecutionActivity`** | **18 kluczy** | **C6 DOSTARCZONE, czeka na odbiór.** ⭐ Migracja to **RE-CIĘCIE ZDAŃ**, nie podmiana słów: szyk i sklejanie fragmentów rozwiązuje zwykła reguła D‑3 w rozdzielczości ZDANIA, a mechanizmu wymagała wyłącznie KATEGORIA liczby (§4.3). ⭐⭐ Dowodem zerowej zmiany treści są **NIETKNIĘTE** `ExecutionSummaryTests` / `ExecutionActivityTests` — forma dualna przez `ExecutionEnglish`. ⚠ Jedna wartość angielska zmieniona świadomie i zgłoszona przed akceptacją kontraktu: fallback sterownika nie miał liczby pojedynczej i renderował `"1 rows affected"`. ⛔ `TableChange.Verb` nie jest już bindowane w XAML — dwa sąsiadujące bindingi (`Count`, `Verb`) zapisywały angielski szyk w UKŁADZIE |
| ↳ ✅ **`DiagnosticsEngine` (ET0001–8)** | **9 kluczy** | **C5 ZROBIONE.** ⭐ Kształt kontraktu **ratyfikowany przed kodem**, bo `Diagnostic` to `readonly record struct`. ⭐ `string Message` → `LocalizableMessage` **bez bliźniaka angielskiego** (zmierzone: `DiagnosticsEngineTests` nigdy nie asertuje treści) ⇒ kształt C2. ⭐⭐ Warunkiem było nadanie `LocalizableMessage` równości strukturalnej — §4.0, gotcha **#358**. ⛔ `ET0008` = **dwa klucze** (rzeczownik nie jedzie jako argument), a klucz mieszka w strukturze właśnie dlatego, że jedna kategoria daje dwa zdania. ⛔ **`ET0004` jest NIEOSIĄGALNE** (żadna ścieżka bindera nie tworzy nierozwiązanej referencji o roli `Parameter`) — nazwany wyjątek z przypiętą przesłanką. ⭐ Live switching: **jedna** subskrypcja na panel odświeża tekst wierszy bez przebudowy (W3) |
| ↳ ⏸ **`KindLabel` / `SymbolKind` — TEMAT ODŁOŻONY** | ~8 wartości | 🔒 **Decyzja użytkownika (2026-08-10): NIE otwierać teraz.** To osobna decyzja **kontraktowa**, nie sprzątanie. Fakty: `QuickInfoEngine.KindLabel` jest **piątą kopią** słownictwa, które etap App skonsolidował i już lokalizuje (`QuickInfoView.KindLabel` → `ObjectKind*`); ⛔ właściwym kształtem **nie jest** zadeklarowanie kluczy rodzajów w Core (druga kopia słów App), tylko oddanie `SymbolKind` jako DANEJ — co zmienia kontrakt `QuickInfoFact`. ⚠ Koszt bieżący: polski czytelnik zobaczy *„Rodzaj: Table"*, gdy drzewo metadanych mówi *„Tabela"*. Dotyczy też `"Active"/"Inactive"`, `"Identity"/"Computed"`, `"Input parameter"` |
| ↳ ✅ **Formy liczby mnogiej — MECHANIZM ZBUDOWANY (C6)** | zmierzone **7** rozgałęzień w kodzie + **30** hedge'ów `(s)` | ⭐ §4.3. ⛔ Odziedziczony licznik „5" **nie przetrwał pomiaru**. Zmigrowane w C6 są WYŁĄCZNIE `Query.Exec.*`; SessionHealth, QuickInfo, Performance i 30 hedge'ów App zostają — każde osobną decyzją. **Zapis historyczny:** Zebrane dotąd: 2 nagłówki `SessionHealthVerdict.Headline` (C1) · QuickInfo `"1 column"`/`"N columns"` (C2) · `ExecutionSummary` „1 row"/„N rows". Mechanizm projektujemy na **pełnym** zestawie. ⚠ Ograniczenie zmierzone: strażnik `NoCode_BranchesOnAParticularLanguage` skanuje `App/Localization/**`, więc tablica reguł per-język zapali go |
| ↳ ⛔ **Performance** | ~70 | moduł ZAMKNIĘTY — otwierany dopiero po osobnej decyzji, po zdobyciu wzorca na modułach otwartych |
| **Polskie tłumaczenie** | 2 186 wpisów | świadomie po uporządkowaniu tekstów |
| **QA wzrokowe na żywym oknie** | — | ⚠ niewykonalne przed tłumaczeniem: przy jednym języku żywy i zamrożony binding renderują ten sam tekst |
