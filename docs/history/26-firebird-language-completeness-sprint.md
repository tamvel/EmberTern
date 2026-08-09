# 26 — Sprint: Firebird language completeness & product polish (2026-08-07)

Sześć zgłoszeń z normalnego użycia, zamkniętych w jednym sprincie na gałęzi `feat/product-polish`,
świadomie **przed M4**. Punkty P1 i P4 zamknięto wcześniej (commit `74d24ba`); ten dokument opisuje
resztę: **P2** (audyt zgodności z gramatyką Firebirda), **P3** (przycinany edytor daty), **P5**
(formaty dat) i **P6** (kursor `FOR SELECT` z `NEW`/`OLD` w debuggerze).

> ⭐ **Jedno zdanie, jeżeli masz przeczytać tylko jedno.** Trzy z czterech punktów po pomiarze okazały
> się czymś innym, niż opisywało zgłoszenie — a w P5 pomiar **odwrócił diagnozę całkowicie**: siatki
> danych nigdy nie były przywiązane do `InvariantCulture`, tylko wiernie odtwarzały nadpisanie formaty
> daty w Windows tego użytkownika. To ta sama lekcja co ze sprintu stabilizacyjnego, o poziom wyżej:
> *zgłoszenie mówi, GDZIE użytkownik to zobaczył, nie CO jest zepsute* — i czasem także **nie to, że
> cokolwiek jest zepsute w tym miejscu**.

---

## P2 — Audyt zgodności parsera, bindera i diagnostyki z gramatyką Firebirda

### Dlaczego to nie mogło być kolejną poprawką

Użytkownik postawił sprawę wprost: *„Nie chcę kolejnej poprawki typu: napraw DATEADD, napraw
AUTONOMOUS, koniec. To już przerabialiśmy kilka razy."* I miał rację co do wzorca — historia tej
rodziny defektów to cztery kolejne poprawki, każda po jednym zgłoszeniu:

| kiedy | zgłoszona konstrukcja | co dodano |
|---|---|---|
| wcześniej | `NEXT VALUE FOR <gen>` | test „poprzedni token to `FOR`" |
| 2026-08-01 | `GEN_ID(<gen>, 1)` | `IsGeneratorNamePosition` (gotcha #302) |
| 2026-08-03 | `EXTRACT(YEAR FROM …)` | `IsDateTimePartPosition` |
| 2026-08-07 | `DATEADD`, `OVERLAY`, okna, … | **ten sprint** |

⭐⭐ **Diagnoza wzorca, i to ona zdecydowała o architekturze rozwiązania: każda z tych poprawek była
predykatem POZYCYJNYM, czyli listą dozwolonych wyjątków — a kompletność takiej listy jest ograniczona
przez zgłoszenia, które ją zbudowały.** Każda niezamodelowana konstrukcja Firebirda była fałszywym
ET0003 czekającym, aż ktoś ją napisze. Nie było sposobu, żeby ta droga się zbiegła.

### Przyczyna źródłowa

Firebird rezerwuje bardzo mało słów. Większość jego własnego słownika — `MONTH`, `PLACING`,
`UNBOUNDED`, `AUTONOMOUS`, `OFB`, `SHA256` — jest **niezarezerwowana**, i to celowo: użytkownik ma
prawo nazwać kolumnę `MONTH`. Te słowa leksują się więc jako zwykłe **identyfikatory**, a oba
spacery wyrażeniowe bindera, widząc identyfikator w pozycji wartości, czytają je jako zmienną (PSQL)
albo kolumnę (zapytanie).

⭐ **Druga połowa defektu nigdy nie trafiła do żadnego zgłoszenia, bo jej objaw jest cichszy.** Spacer
**zapytaniowy** nie miał żadnej bramki gramatycznej — ani jednej. Tam, gdzie PSQL zgłasza ET0003na
`DATEADD(MONTH, …)`, zapytanie **po cichu wiąże** `MONTH` z kolumną o tej nazwie, jeżeli jedna
tabela w zasięgu taką ma (zły kolor, złe Quick Info, złe find-references), a przy dwóch zgłasza
ET0005 *Ambiguous column*.

### Instrument: korpus zgodności z Firebird Language Reference

Zamiast zbierać zgłoszenia — przejść **rozdział po rozdziale** przez Language Reference i zebrać
konstrukcje, w których gramatyka stawia gołe słowo tam, gdzie inaczej czytałoby się wyrażenie.
Powstał `SqlTestCorpus.LanguageReference` (80 pozycji): części daty/czasu, funkcje łańcuchowe z
gniazdami słów, `CAST`, kryptografia (`USING`/`MODE`/`KEY`/`IV`), strefy czasowe FB4, pełna gramatyka
ramki okna, `IN AUTONOMOUS TRANSACTION`, gramatyka opcji `EXECUTE STATEMENT`, słownik obsługi
wyjątków, etykiety pętli, kursory, klauzule zapytań i słownik DDL.

⭐ **Korpus wszedł do `SqlTestCorpus.All`, więc każda z tych 80 konstrukcji przechodzi teraz także
przez niezmienniki §0 formatera (round-trip bajtowy + idempotencja) i przez harness różnicowy AST.**
Jeden korpus, trzy strażniki — i to wyjaśnia, dlaczego suite urósł o ~880 testów przy ~90 nowych
pozycjach.

**Pomiar wyjściowy (przed jakąkolwiek zmianą): 26 z 80 pozycji korpusu produkowało fałszywe
znaleziska albo błędne wiązania.** Zgłoszone konstrukcje były podzbiorem — większość z tych 26 nigdy
nie została zgłoszona.

### Rozwiązanie: dwa uzupełniające się mechanizmy, każdy z właściwą gwarancją

Kluczowa obserwacja: **słownik i pozycja odpowiadają na różne pytania i mają różne konsekwencje**,
więc jeden mechanizm nie mógł obsłużyć obu połówek.

**1. `FirebirdGrammar` — wiedza POZYCYJNA (nowa klasa Core, jeden właściciel).**
Przejęła oba istniejące predykaty z `SemanticBinder` i uogólniła je:

- gniazdo jednostki — token zaraz po `EXTRACT(` / `DATEADD(` / `DATEDIFF(`, **bez** wymogu słownika
  (wymóg kosztowałby fałszywe ET0003 przy każdym naciśnięciu klawisza w niedopisanym `EXTRACT(YEA`);
- wewnątrz listy argumentów **jawnie wymienionej** funkcji ze słownymi gniazdami (`OVERLAY`, `HASH`,
  `ENCRYPT`, `NTH_VALUE`, …) — słowo ze słownika Firebirda;
- wewnątrz specyfikacji okna `OVER (…)` / `WINDOW w AS (…)` — słowo ze słownika;
- pozycja TYPU w `CAST(x AS …)` — a jeżeli nazwa jest domeną, **rozwiązuje ją** (ten sam kolor i
  Ctrl+Click, co domena w pozycji typu `DECLARE VARIABLE`, D15.1);
- mała tablica fraz (`AT TIME ZONE`, `NULLS FIRST`, `TYPE OF COLUMN`, …).

⭐ **Filtr słownikowy jest tu tylko tanim wstępnym testem, a decyzję podejmuje konstrukcja** — dzięki
temu `SELECT MONTH FROM SALES` nadal wiąże swoją kolumnę. ⛔ Lista funkcji jest **jawna, nie „każda
funkcja"**: w `COALESCE(MONTH, 0)` to słowo JEST kolumną i reguła blankietowa zabrałaby jej wiązanie.

**Ta sama bramka trafiła do spacera ZAPYTANIOWEGO**, który wcześniej nie miał żadnej.

**2. `FirebirdSyntax.IsNonReservedWord` — wiedza SŁOWNIKOWA (kompletność).**
Słownik niezarezerwowanych słów Firebirda, przepisany z dodatku Language Reference. Jego jedyną
dozwoloną konsekwencją jest **milczenie**: identyfikator, który jest słowem Firebirda i nie rozwiązał
się do niczego, nie jest *dowodliwie* nieznaną zmienną. ⭐ Ograniczony **JĘZYKIEM**, a nie
zgłoszeniami — i język jest skończony, udokumentowany i nie rośnie wraz z użyciem.

⚠ **Nigdy nie tłumi WIĄZANIA.** Zmienna naprawdę nazwana `MONTH` jest zadeklarowana, więc się
rozwiązuje i zachowuje referencję, kolor, Quick Info i find-references.

### ⭐⭐ Trzecia decyzja, wymuszona przez istniejący test — i to ona uratowała precyzję

Pierwsza wersja tłumiła ET0003 dla **każdego** nierozwiązanego słowa ze słownika. Przewrócił ją
istniejący strażnik `TheSameWord_OutsideExtract_IsStillFlagged`, którego komentarz mówił dokładnie,
czego pilnuje: *„The word is not exempt — only the position is… which is what stops this fix from
becoming a silent hole named YEAR."*

Autor tamtego testu miał rację, a moja pierwsza reguła była za tępa. Poprawka:
**`FirebirdGrammar.IsVocabularyInsidePhrase`** — słowo ze słownika jest tłumione tylko wtedy, gdy
**sąsiaduje z innym słowem**. `YEAR` samotny między `=` a `;` jest operandem i niczym innym;
`USING SHA256`, `AT LOCAL`, `UNBOUNDED PRECEDING`, `OF MONTH` to pary, które gramatyka czyta razem.

⚠ To wymusiło jeszcze jedno rozróżnienie, i ono też jest merytoryczne: **zmienna kontekstowa
(`ROW_COUNT`, `SQLCODE`, `USER`, `INSERTING`) JEST kompletnym wyrażeniem sama w sobie**, więc
`v = row_count;` jest poprawnym Firebirdem i musi milczeć bezwarunkowo — inaczej niż `v = year;`.
Stąd dwa zbiory w `FirebirdSyntax`, tłumione pod różnymi warunkami; sklejenie ich w jeden czyni jeden
z tych dwóch przypadków błędnym, którykolwiek warunek się wybierze.

Przy okazji: dawny prywatny zbiór `BareContextVariables` w `SemanticBinder.Psql` — ta sama myśl
trzymana w drugim miejscu, tłumiąca dokładnie dziewięć słów i nic więcej — **zniknął**. Jeden
właściciel.

### 🐞 Znalezisko strukturalne, którego nie było w zgłoszeniu

Podczas pomiaru wyszło coś poważniejszego niż kolor squiggle'a: **`ParsePsqlUnit` rozgałęział się
wyłącznie po PIERWSZYM tokenie**, więc instrukcja z PREFIKSEM w ogóle nie była rozpoznawana i
spadała do `ParsePsqlLeaf`, który kończy na **pierwszym średniku**.

```
retry: while (i < 10) do begin i = i + 1; leave retry; end
                                        ↑ tu liść się kończył
```

Liść zawierał wtedy `=` na poziomie zerowym, więc `ClassifyLeaf` uznawał go za **przypisanie** — a to
jest pozycja, w której nierozwiązana gola nazwa JEST zgłaszana. Więc: etykieta raportowana jako
nieznana zmienna, **a ciało pętli w ogóle niezamodelowane**. Ten sam kształt co gotcha #301
(`EXECUTE BLOCK`), o jedną konstrukcję dalej. To samo dotyczyło
`IN AUTONOMOUS TRANSACTION DO BEGIN … END`.

Poprawka: `FirebirdGrammar.StatementPrefixLength` (jedna definicja) + `TryConsumeStatementPrefix` w
parserze, który zjada prefiks i przekazuje jego indeks startowy dalej, żeby węzeł go objął.

⚠ **Obie formy wymagają, by po prefiksie stała instrukcja ZŁOŻONA** — i to zawężenie jest decyzją, nie
przeoczeniem. Naprawiany defekt to liść połykający średnik należący do zagnieżdżonej instrukcji, co
może się zdarzyć tylko wtedy, gdy następna instrukcja ma zagnieżdżone instrukcje. Prefiksowana
pojedyncza instrukcja (`IN AUTONOMOUS TRANSACTION DO INSERT …;`) i tak kończy się na własnym `;`.

⚠⚠ **I jedna połowa poprawki bez drugiej nic by nie dała.** Po naprawie parsera etykieta jest już
częścią tokenów węzła `WhileStatement`, a `BindControlHeader` chodzi po nich **z włączonym
zgłaszaniem** — więc ET0003 na etykiecie zostawał. Dlatego `StatementPrefixLength` jest **publiczne i
wspólne**: parser go używa, żeby DOJŚĆ do instrukcji, binder — żeby jej nagłówka nie zaczynać od
etykiety. Dwa konsumenty, przeciwne powody, jedna definicja.

### Wynik

**80/80 pozycji korpusu zielone** (było 26 czerwonych). Zgłoszone konstrukcje działają; działa też
większość, której nikt nie zgłosił.

---

## P3 — Przycinany `CalendarDatePicker` w siatce danych

Zgłoszenie nie nazywało wymiaru, a kandydaci byli dwaj i mieli różne przyczyny. Zgodnie z instrukcją
(*„Nie zakładaj przyczyny. Najpierw zmierz problem."*) — pomiar drzewa wizualnego przed jakąkolwiek
zmianą.

**Pomiar (pionowo):** `CalendarDatePicker` z motywu Fluent ma **własne `MinHeight` = 32**, podczas gdy
jego zawartość prosi o **24** (`PART_TextBox` 22, `PART_Button` 24). Wiersz `data-edit` ma **stałe**
`Height="32"` (gotcha #322 — nie ma jak urosnąć), a padding komórki `6 2` zabiera 4, więc na edytor
zostaje **28**. 32 > 28 ⇒ obcięcie, bez błędu i bez skoku układu, który by je pokazał.

⭐ Poprawką jest **ROLA, a nie dobrana liczba**: `Size.Control` — ten sam, który bierze `TextBox` w
sąsiedniej komórce. Zawartość mieści się w 24 z zapasem, więc nic nie zostaje ściśnięte w środku;
obniżamy tylko żądanie kontrolki do jej rzeczywistej potrzeby. Przy okazji znika drugi objaw: edytor
daty przestaje być wyższy od pozostałych edytorów tej samej siatki.

**Druga połowa, pozioma:** kontrolka miała lokalne `MinWidth = 120`. Siatka danych **pamięta
szerokości kolumn** (`GridLayoutBehavior.GridId="TableDetail.Data"` zapisuje je i przywraca jako
`DataGridLength(px)`), więc zwężona kolumna daty wraca po restarcie węższa niż 120 — i edytor nie
mieści się w swojej komórce w poziomie. `MinWidth` usunięty: **rozmiar nadaje pojemnik, element go
przyjmuje** (decyzja architektoniczna 2 z M2b), a wymuszony `MinWidth` jest dokładnie odwrotnością tej
reguły.

⚠ Strażnik (`GridDateEditorTests`) asertuje żądaną wysokość **względem miejsca, jakie komórka
faktycznie zostawia**, a nie względem liczby — i osobno pilnuje, że geometria wiersza w widoku jest
nadal tą, na której poprawka stoi. Pilnujemy PRZESŁANKI, nie POLITYKI (#322).

---

## P5 — Formaty dat: ⭐⭐ pomiar odwrócił diagnozę

Zgłoszenie: *„Obecny format dat w aplikacji mi nie odpowiada. Nie chcę sztywnego europejskiego
formatu."* Oczywista hipoteza: gdzieś jest zaszyty format albo `InvariantCulture` na ścieżce
prezentacji.

**Pomiar mówi coś innego, i to jest najważniejszy wynik tego punktu.** Obie siatki danych renderują
przez `CultureInfo.CurrentCulture`:

```
MACHINE culture   = pl-PL short=yyyy-MM-dd => 2026-08-07 14:05:09
pl-PL no override = short=d.MM.yyyy        => 7.08.2026 14:05:09
ToString(InvariantCulture)                 => 08/07/2026 14:05:09
Binding StringFormat "{0}"                 => 2026-08-07 14:05:09
```

⭐ **Wrażenie „sztywnego formatu ISO" pochodzi z nadpisania formatu daty w Windows tego użytkownika**
(`pl-PL` z krótką datą ustawioną na `yyyy-MM-dd`) — czyli aplikacja robiła dokładnie to, o co
zgłoszenie prosi, tylko system mówi jej co innego, niż użytkownik się spodziewał. **Nie ma tu defektu
do naprawienia**, i powiedzenie tego wprost jest wynikiem, nie uchyleniem się.

Co **było** zaszyte, po audycie całego drzewa `src/`:

| miejsce | było | jest |
|---|---|---|
| About → data wydania | `d MMMM yyyy` + `InvariantCulture` ⇒ **angielska nazwa miesiąca na każdej maszynie** | długa data kultury czytelnika |
| Historia parametrów | zaszyte `yyyy-MM-dd HH:mm` | data + czas kultury czytelnika |
| Znaczniki w logach (Messages / Trace / Executed SQL) | trzy niezależne wzorce | jeden `DateTimeDisplay.LogTime` |

⭐ Powstał **jeden właściciel prezentacji daty** — `EmberTern.Core.Formatting.DateTimeDisplay`.
Uzasadnienie nie jest „na przyszłość": użytkownik zapowiedział wybór formatu w Settings Center, a
preferencja potrzebuje **jednego miejsca, w którym zadziała**; rozsypana kultura zamieniłaby ten krok
w wyszukiwanie z zamianą po całej aplikacji, czyli w defekt „ustawienie działa wszędzie poza jednym
ekranem".

⚠ **Jeden świadomy wyjątek, zapisany jako decyzja:** `LogTime` zostaje 24-godzinny i stałej
szerokości, bo jego odbiorcy to KOLUMNY LOGU — znaczniki czyta się w dół kolumny i porównuje między
sobą, a kultura 12-godzinna zmieniałaby długość kolejnych wierszy.

⛔ Strona maszynowa **nie ruszona i przypilnowana**: `SqlLiteralWriter` (Copy as INSERT, eksport
`.sql`), `ImportValueConverter`, nazwy plików kopii `settings.dat`, parsowanie daty wydania z
`Directory.Build.props`, logi diagnostyczne. `DatePresentationTests` trzyma listę tych plików **wraz z
powodem** — wartością listy nie są nazwy, tylko to, że dopisanie się do niej zmusza autora do
powiedzenia, po której stronie granicy stoi. Strażnik zweryfikowany podsadzeniem naruszenia.

---

## P6 — Debugger: kursor `FOR SELECT` odwołujący się do `NEW`/`OLD`

Ograniczenie z D10 brzmiało: *„a FOR SELECT cursor that references NEW/OLD is not supported in a
trigger — step over the loop."*

⭐⭐ **Przesłanka tamtej odmowy była PRAWDZIWA, a wniosek fałszywy.** To prawda, że syntetyczne zmienne
kontekstowe harnessu (`ET_CTX_i`) nie istnieją wewnątrz osobno otwartego kursora DSQL. Ale kursor
nigdy ich nie potrzebował: **`NEW.ID` w zapytaniu kursora jest WARTOŚCIĄ, a ramka już ją ma.** Więc
referencja zostaje przepisana na pozycyjne `?` i związana z ramki — dokładnie tak, jak od D6 jest
traktowane `:zmienna`.

Zero nowego mechanizmu: to samo przepisanie po SPANIE, ta sama lista parametrów, ten sam binder.
Zmiana to trzy rzeczy:

1. `ContextSubstitution.ReferencesIn` — wyciągnięte z `Substitute` i **upublicznione**, bo Cursor
   Bridge potrzebuje tej samej reguły parowania `RecordAlias` + `Column` do innego celu. Dwa
   konsumenty, dwa przepisania, **jedna definicja** tego, czym jest referencja kontekstowa i która
   zmienna ramki trzyma jej wartość — druga kopia tego parowania to prosta droga do przepisania,
   które nie zgadza się z iniekcją, którą karmi.
2. `CursorBridge.Build` przyjmuje opcjonalnie model + kontekst wyzwalacza i dokłada te referencje do
   **tej samej** listy parametrów, posortowanej po pozycji w źródle.
3. `FirebirdDebugExecutor.OpenCursor` przestaje odmawiać i przekazuje kontekst dalej.

⚠ **Wiązanie przy OPEN to jest to, co robi sam Firebird**, i dlatego jest to wierne, a nie tylko
wygodne: skompilowany wyzwalacz oblicza parametry kursora raz, przy otwarciu, więc ciało
przypisujące `NEW.col` W TRAKCIE pętli nie zmienia wierszy już otwartego kursora. Odczyt ramki raz,
przy otwarciu, odtwarza to; odczyt przy każdym `FETCH` byłby wyborem NIEwiernym.

⛔ Martwy po zmianie `QueryReferencesContext` **usunięty**, nie zostawiony (Contract #20).

**Weryfikacja na żywym FB5** — bez tego punkt się nie liczy (zasada `sim == real`). Lab dostał
`TRIG_CURSOR_LAB` + `TR_CURSOR_BU`, a `DebuggerFidelityProbe` przypadek **40**:

```
=== 40. TR_CURSOR_BU — BEFORE UPDATE whose body is a FOR SELECT cursor referencing NEW (sim == real) ===
  PASS  cursor-over-NEW ⇒ NEW.NOTE (sim == real)  — sim 'L=2/3' == real 'L=2/3'
  PASS  the case is discriminating (the cursor really matched NEW.ID's rows)  — L=2/3

ALL PASS
```

⚠ Przypadek musi być **ROZRÓŻNIAJĄCY**, nie tylko równy: `'L=0/0'` to wynik kursora związanego z
`NULL`-owym `NEW.ID` — odpowiedź wyglądająca całkiem sensownie, która przeszłaby sprawdzenie
równości, dowodząc czegoś przeciwnego (ta sama pułapka, którą zapisuje przypadek 39). Stąd osobna
asercja na `L=2/3`.

⚠ Nowy wyzwalacz dostał **własną tabelę**, nie `TRIG_SUBQ_LAB` — drugi `BEFORE UPDATE` piszący
`NEW.NOTE` na tej samej tabeli pozwoliłby kolejności wykonania zdecydować o wartości, którą asertuje
istniejący przypadek 17 (gotcha #248).

---

## Weryfikacja całości

| co | wynik |
|---|---|
| build Debug | 0 ostrzeżeń / 0 błędów |
| build Release | 0 ostrzeżeń / 0 błędów |
| suite (partycja główna) | **8131** zielone |
| suite (partycja headless zbiorcza) | **77** zielone |
| suite (partycja headless izolowana) | **55** zielone |
| **razem** | **8263** |
| smoke Debug + Release | uruchamia się, `Responding`, zero `FATAL` |
| `DebuggerFidelityProbe` (żywy FB5) | **40/40 ALL PASS** |
| `ChangeSafetyProbe` (żywy FB5, po przebudowie labu) | **ALL PASS** |

⚠ Skok z 7378 do 8263 to w większości **nie nowe asercje pisane ręcznie**: 80 pozycji korpusu wchodzi
do `SqlTestCorpus.All`, które zasila teorie formatera (§0 round-trip, idempotencja, casing) i harness
różnicowy AST — ~11 przebiegów na pozycję.

⚠ `GridDateEditorTests` dołącza do partycji headless zbiorczej (konstruuje kontrolki Avalonii), więc
filtr partycji urósł o tę nazwę.

---

---

## QA użytkownika (2026-08-08) — trzy poprawki, wszystkie wokół tej samej granicy

QA przeszło, ale wywołało trzy zgłoszenia. ⭐ Wszystkie trzy są konsekwencją tej samej rzeczy, którą P3 i P5
otworzyły: **skoro edytor daty wreszcie się mieści i skoro prezentacja dat ma jednego właściciela, widać, że
o formacie i o wyborze kontrolki decydowała dotąd strona CLR, a nie typ kolumny Firebirda.**

### Q1 — edytor `CalendarDatePicker` na kolumnie TIMESTAMP

⚠ **Defekt był groźniejszy, niż opisywało zgłoszenie.** Widoczna połowa to „nie da się edytować czasu";
prawdziwa to `SelectedDate` — **zatwierdzenie wybranej daty zapisywało północ na miejsce godziny, którą wiersz
już miał**. Cichy zapis wartości, której użytkownik nie wybrał (reguła #11, gotcha **#329**).

⭐ Wybór zamiennika to **pomiar frameworka, nie założenie** (wprost poproszony przez użytkownika): Avalonia
12.1.1 udostępnia `CalendarDatePicker`, `DatePicker`, `TimePicker`, `Calendar` — i **żadnej kontrolki łączącej
datę z czasem**. Sklejenie dwóch w komórce o 24 px byłoby własnym kompozytem, więc TIMESTAMP edytuje się jako
tekst: jedyny edytor, który potrafi wyrazić całą wartość.

⭐ **Zatwierdzenie parsuje tekst na typowany `DateTime`, nie oddaje napisu serwerowi** — i to jest połowa
bezpieczeństwa tej poprawki. Firebird czyta literał po SEPARATORZE (`07/08/2026` to dla silnika 8 lipca),
podczas gdy siatka pokazuje pisownię kultury czytelnika. Kolejność: forma silnika po dokładnym kształcie →
kultura czytelnika → invariant → tekst dosłownie (wtedy odmowę wystawia Firebird w istniejącym banerze).
⚠ `TIMESTAMP WITH TIME ZONE` i `TIME` zostają tekstem bez typowania — wartość ze strefą nie jest `DateTime`,
więc parsowanie zgubiłoby strefę.

### Q2 — format dat w oknie Variables / Context debuggera

Panel renderował wszystko przez `InvariantCulture`, więc TIMESTAMP czytał się jako `08/07/2026 00:00:02` —
amerykańska data na polskiej maszynie i pisownia, której **sam Firebird nigdy nie drukuje**.

⭐ Reguła: **w debuggerze formą czytelną jest forma SILNIKA** (`yyyy-MM-dd [HH:mm:ss]`), bo czytelnik porównuje
to, co widzi, z `isql`, ze źródłem, po którym kroczy, i z literałami, które zaraz wpisze do Watcha. Rodzina
`DateTimeDisplay.Firebird*` to trzecia kategoria obok kultury czytelnika i strony maszynowej — **prezentacja,
ale mierzona silnikiem**. Objęła Variables/Context, Watche, wartości inline (czytają `ValueText`) oraz zasiew
pola edycji; round-trip pinowany **przez prawdziwy parser zatwierdzania**, nie przez jego kopię w teście.
⛔ Liczby nietknięte — invariant to konwencja literałów harnessu, nie decyzja prezentacyjna.

### Q3 — `00:00:00` na kolumnie DATE, czyli decyzja po typie CLR

⛔⛔ **Najtrwalsza z trzech, i to użytkownik nazwał regułę: format ma wynikać z typu metadanych Firebirda, nie
z typu CLR.** `DATE` i `TIMESTAMP` docierają jako ten sam `DateTime`, więc renderer pytający WARTOŚĆ dopisywał
`00:00:00` do kolumny, która czasu w ogóle nie przechowuje. ⚠ Kusząca naprawa — „ukryj czas, gdy północ" —
jest gorsza: ukrywa **prawdziwe** `00:00:00` na TIMESTAMP, czyli zamienia defekt widoczny na niewidoczny
(gotcha **#330**).

Nowe `DateTimeDisplay.CellForType(value, firebirdType)` bierze typ kolumny i zwraca `null` dla wszystkiego,
co nie jest czasowe — ten `null` jest seamem, bo nie wolno mu przejąć formatowania liczb. Siatka danych
przekazuje `FieldInfo.BaseTypeName`. ⭐ Przy okazji **usunięty martwy `Cell(object?)`**, który rozstrzygał
dokładnie tą heurystyką `TimeOfDay == 0` i **nie miał żadnego konsumenta w produkcie** — martwy pomocnik
ucieleśniający złą regułę to defekt czekający na pierwszego wywołującego (Contract #20).

⚠ **Zakres zmierzony i zawężony świadomie: siatka wyników SQL tego nie dostaje**, bo `QueryColumn` niesie
`Name` + **`Type ClrType`** i nie ma typu Firebirda. Rozszerzenie oznaczałoby przeciągnięcie metadanych typu
przez egzekutor — osobna zmiana, nie poprawka QA.

### Q4 — precyzja edytora do sekundy, i pułapka, którą to otworzyło

Użytkownik: milisekundy są potrzebne rzadko i utrudniają ręczną edycję ⇒ **zasiew edytora kończy się na
sekundach** (wpisanie ułamka nadal dozwolone). ⚠⚠ Ale `DataGrid` zatwierdza komórkę dlatego, że **wyszedł z
niej fokus**, a nie dlatego, że coś wpisano — więc sam ten wybór zamieniłby przejście Tabem przez wiersz w
**zapis zaokrąglonej wartości na miejsce ułamkowej, której nikt nie dotknął** (gotcha **#331**).
Stąd reguła „nic nie wpisano ⇒ nic nie zapisano": ścieżka zatwierdzania porównuje tekst edytora z zasiewem dla
bieżącej wartości. ⭐ Nośne jest to, że **zasiew ma JEDNEGO właściciela** (`EditorSeedText`) — szablon edycji i
sprawdzenie czytają to samo, bo dwie kopie „od czego zaczyna to pole" prędzej czy później nie zgodziłyby się co
do tego, czy użytkownik cokolwiek wpisał.
⚠ Debugger **zachowuje pełną precyzję** (`FirebirdTimestamp` vs `FirebirdTimestampToSecond`) — tam wartość się
OGLĄDA, a nie przepisuje, i ukryty ułamek fałszowałby stan ramki. Dwie metody, dwie decyzje, każda ze swoim
powodem.

### Weryfikacja QA

| co | wynik |
|---|---|
| build Debug / Release | 0 ostrzeżeń / 0 błędów |
| suite | **8291** (8145 + 91 + 55) |
| smoke Debug + Release | `Responding`, zero `FATAL` |
| `DebuggerFidelityProbe` (żywy FB5) | **40/40 ALL PASS** |

⭐ **Wszystkie cztery nowe strażniki zweryfikowane podsadzeniem naruszenia, każdy osobno i w jednym wymiarze.**
Dwa warte odnotowania: usunięcie formatera silnika wywala 3 testy wyświetlania i **nie rusza** testów zasiewu
edycji (dowód, że oba wymiary są pinowane niezależnie), a test `ADateColumn_ShowsNoTimeAtAll` asertuje **tekst,
który komórka faktycznie renderuje**, a nie sam formater — bo zepsuło się właśnie *dotarcie typu kolumny do
szablonu*, a test na formaterze przeszedłby z przeciętym okablowaniem (kształt #315).
⚠ `ATime_IsShownAsAClock` dostał ułamek, żeby był **rozróżniający**: na pełnych sekundach `TimeSpan` formatuje
się invariantnie identycznie, więc pierwotna wersja przeszłaby z usuniętą poprawką.

🐞 **Znalezisko poboczne, naprawione przy zamykaniu:** filtr partycji testów w `CLAUDE.md` nie zawierał
`GridDateEditorTests`, choć notatka tego samego sprintu mówiła, że klasa dołącza do partycji headless. To
odwrotność znanej pułapki „nazwa, która nic nie łapie": klasa headless liczyła się do partycji głównej. Liczby
sprintu były poprawne (77 zawierało jej 5 testów), zgnił wyłącznie napis w dokumencie — #284 jeszcze raz.

---

## Co zostaje otwarte

- ⏸ **Wybór formatu daty w Settings Center** — zapowiedziany przez użytkownika, nie zbudowany w tym
  sprincie. `DateTimeDisplay` jest miejscem, w którym się podepnie; dziś nie ma tam żadnej opcji.
- ⏸ **Etykieta pętli zapisana bez spacji** (`retry:while`) — lekser produkuje jeden token `Parameter`
  `:WHILE`, więc słowo kluczowe instrukcji jest wewnątrz tokenu, na którym nikt nie może się
  rozgałęzić. Rozdzielenie oznaczałoby nauczenie leksera, które słowa otwierają instrukcję złożoną,
  czyli przeniesienie gramatyki do tokenizacji za pisownię, której nikt nie używa. Zapisane, nie
  naprawione.
- ⏸ **`BindBareReference` w zapytaniu rozwiązuje gołą nazwę do LOKALNEJ przed kolumną**, podczas gdy
  Firebird woli kolumnę — otwarte od sprintu stabilizacyjnego, nadal warte własnego pomiaru.
