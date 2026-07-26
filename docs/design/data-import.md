# Data Import — dokument projektowy modułu importu danych

**Status: 🔒 PROJEKT ZAMROŻONY (2026-07-26). Etapy I0–I6 wykonane i zaakceptowane.**
Następny krok: **etap I7** (Podgląd po konwersji, uruchomienie, raport — koniec MVP) z §6.
**I5 przeszedł dwa przeglądy wzrokowe użytkownika (2026-07-26) i został ZAMKNIĘTY** po szwie domykającym,
który dostarczył 10 z 12 uwag UX (U1–U12, §3.8) i **zrewidował układ powierzchni — §3.1 opisuje stan
obowiązujący**. Poza modułem świadomie zostają: **U4** (gęstość kontrolek → osobny sprint UX całego
EmberTerna **po** zamknięciu modułu, decyzja użytkownika) i **U5** (responsywność → weryfikacja przy I6).
Szczegóły — blok „📍 STAN IMPLEMENTACJI" niżej.

> ### 🔒 DOKUMENT ZAMROŻONY — obowiązuje od 2026-07-26 (po akceptacji wyników I0)
>
> Etap I0 zakończył się dokumentem **[data-import-i0-findings.md](data-import-i0-findings.md)**
> (8 rekomendacji, wszystkie **zaakceptowane** przez użytkownika 2026-07-26). Werdykt: architektura stoi;
> jedna korekta sygnatury portu (`IImportWriter`, REK-1) została przyjęta i **jest już wniesiona poniżej**.
> **Zamrożenie obowiązuje: od tego momentu nie wracamy do zmian projektowych — wyłącznie implementacja
> według §6.**
>
> - Dokument zmienia się już tylko **w miejscu** i tylko o **stan faktyczny** („as-built": co zostało
>   dostarczone, jakie gotchy powstały) — nigdy o decyzje projektowe.
> - Odkrycie w trakcie implementacji, które naprawdę podważa projekt, nie jest wymówką do
>   przeprojektowania po cichu: **zatrzymujemy etap, raportujemy użytkownikowi i czekamy na decyzję**
>   (kontrakt wdrożenia etapowego — `memory/feedback_staged_implementation_contract.md`).
> - Wszystko, co I0 ustalił, jest wniesione w miejsca wskazane w §6 dokumentu wyników; każda taka
>   wstawka jest oznaczona **„(I0)"**, żeby było widać, co jest decyzją projektową, a co wnioskiem
>   z pomiaru.

Historia wersji dokumentu:
- **v1** (2026-07-25) — pierwsza wersja, UI jako klasyczny 6-krokowy kreator.
- **v2** (2026-07-26) — **UI przebudowane na jedną powierzchnię roboczą z sekcjami** (kreator i
  `BreadcrumbBar` usunięte całkowicie); decyzje D1–D6 zatwierdzone; **profile importu przeniesione z
  „przyszłych rozszerzeń" do fundamentu architektury** (§4.8) — model konfiguracji i jego trwałość
  powstają w MVP, nazwane profile są później wyłącznie UI nad tym samym magazynem.
  Architektura Core / Pipeline / Provider / Writer **bez zmian** (zaakceptowana w v1).
- **v3** (2026-07-26) — **wyniki etapu I0 wniesione; dokument ZAMROŻONY.** Jedna zmiana projektowa:
  skorygowana semantyka portu `IImportWriter` (§4.3, decyzja **D9**), bo pomiar wykazał, że
  `FbBatchCommand` jest 16× szybszy **i** podaje indeks błędnego wiersza. Reszta to wnioski z pomiarów
  wniesione jako doprecyzowania (oznaczone **„(I0)"**): obowiązkowa walidacja charsetu połączenia
  (§4.4 krok 4, R1 przeredagowane na **cichą korupcję**), mapowanie błędów na **wektorze GDS** (I4),
  wnioskowanie typów na całym źródle (I8, nowe R19), XLSX wyłącznie SAX + siedem wytycznych providera
  (I9, R8/R20), zmierzone wartości domyślne (§4.5), rozstrzygnięte R7. Pomiary: §11 (skrót) i
  [data-import-i0-findings.md](data-import-i0-findings.md) (pełny).

Dokument jest samowystarczalny: przyszła sesja implementacyjna zaczyna dowolny etap z §6 **bez ponownej
analizy**. Wzorce: `docs/design/execution-modes-and-export-framework.md` (framework eksportu — lustrzane
odbicie tego modułu) oraz `docs/design/firebird-debugger-implementation-plan.md` (kontrakt wykonawczy).

---

## 📍 STAN IMPLEMENTACJI — czytaj to pierwsze (aktualizowane po każdym etapie)

| | |
|---|---|
| **Gałąź** | `feat/data-import` (odbita od `master` @ `d474b42`); **wypchnięta na `origin`**, `private` do dosłania przy najbliższym zamknięciu etapu. Żywe gałęzie repozytorium: `master` + `feat/data-import` |
| **Ostatni commit** | **`95ae39e`** — szew domykający I5: rewizja układu powierzchni + U1/U2/U3/U6/U7/U8/U9/U10/U11 (poprzedni: `0c5667e`, etap I5) |
| **Etapy zamknięte** | **I0** (sondy, `5e90435`) · **I1** (modele, konfiguracja, magazyn, czytnik, `77eb997`) · **I2** (konwersja, mapowanie, walidacja, gotowość, `392850f`) · **I3** (pipeline + dry-run + provider, `434daeb`) · **I4** (Firebird + weryfikacja na żywym FB5, `3b31a4d`) |
| **✅ I5 — ZAMKNIĘTY (2026-07-26)** | Przegląd wzrokowy dał 5 uwag (U1–U5) + 5 propozycji z autoprzeglądu (U6–U10) + U11 + U12 z drugiego oglądu. **Wszystkie rozstrzygnięte; 10 dostarczonych w szwie domykającym** (§3.8). Układ **zrewidowany i wniesiony w miejsce do §3.1** — gwiazdka na powierzchni roboczej, pas A usunięty, kafelki pionowe z zawsze żywym pickerem, grupy ustawień jako karty. **Układ zaakceptowany przez użytkownika.** Otwarte świadomie: **U4** (gęstość globalna → sprint UX po module) i **U5** (weryfikacja przy I6) |
| **✅ I6 — ZAMKNIĘTY (2026-07-26)** | Sekcja **Cel** (istniejąca tabela) + panel **Mapowanie** + łańcuch przeliczeń rozszerzony o cel i mapowanie. Potwierdzony wzrokowo przez użytkownika; odstępstwo o `COUNT(*)`, nazwy triggerów, orientacja „cel → źródło" i reguły identity — **zaakceptowane bez zmian** |
| **Następny etap** | **I7** — Podgląd po konwersji + `Waliduj` + tryby transakcji + `Importuj`/F5 + postęp + raport + Commit/Rollback + „ostatnio użyta" konfiguracja. **Koniec MVP** |
| **Testy** | **5583 zielonych**, 0 niepowodzeń (I6 dodał +15; wszystkich testów importu jest teraz **319**) |
| **Weryfikacja na żywo** | `tools/probes/DataImportProbe` przeciwko FB5 `WI-V5.0.3.1683` — **20/20 ALL PASS** (klasyfikacja błędów + atrybucja wiersza, zachowanie paczek, obowiązki writera, charset) |
| **Build** | 0 ostrzeżeń / 0 błędów (`TreatWarningsAsErrors`) · smoke: aplikacja startuje |
| **Kod w `src/`** | `EmberTern.Core/Import/**` + trzy pliki w `EmberTern.Firebird` + **cztery VM-y i widok w `EmberTern.App`**. Rdzeń nadal ma zero Avalonia, zero `FirebirdSql`, zero UI. |
| **⭐ Kamień milowy** | **Po I4 cały silnik jest gotowy i zweryfikowany na żywym silniku.** Od I5 pracujemy wyłącznie nad interfejsem — pipeline, writer i mapowanie błędów są zamknięte. |

### Zakres pozostały modułu (stan po I5)

| Etap | Co zostało | Blokady / zależności |
|---|---|---|
| ~~**I6**~~ ✅ | sekcja **Cel** (istniejąca tabela) + panel **Mapowanie** + przeliczanie łańcuchowe z anulowaniem | dostarczone; układ rozstrzygnięty przed implementacją, więc I6 wstawił się w gotową ramę |
| **I7** | **Podgląd po konwersji** + `Waliduj` + tryby transakcji + `Importuj`/F5 + postęp + raport + Commit/Rollback + eksport raportu + „ostatnio użyta" konfiguracja | pas **B** (pasek poleceń) powstaje dopiero tutaj — dziś nie istnieje, bo nie miałby czym sterować; **koniec MVP** |
| **I8** | nowa tabela: `ColumnTypeInferencer` (skan całego źródła, R19), edytowalna siatka typów, podgląd DDL, wykonanie na linii Ddl, `DROP` przy niepowodzeniu | rozbudowuje sekcję Cel z I6 |
| **I9** | XLSX + `EmberTern.Export.Office` → `EmberTern.Office`; `XlsxImportProvider` (7 wytycznych); rozgałęzienie sekcji Format po `Capabilities` | zamyka `DataImportXlsxProbe` |
| **I10** | schowek (App czyta, Core parsuje) + `XlsImportProvider` (BIFF8, nowa zależność) | — |
| **I11** | **nazwane profile (UI)** — selektor, „Zapisz jako…", zmiana nazwy, usuwanie | ⭐ **dowód projektu**: jeżeli wymaga zmiany choćby jednego modelu, §4.8 zostało po drodze naruszone |
| **I12** | domknięcie: `docs/history/`, `docs/gotchas.md`, CLAUDE.md, audyt UI w obu paletach + **1366×768**, pomiar na 1 M wierszy | audyt UI wchłania to, co zostanie z U1–U10 |

### Co fizycznie istnieje po I5

```
src/EmberTern.App/               ⭐ I5 — pierwszy kod, który widać na ekranie
    ViewModels/
        DataImportTabViewModel.cs        ⭐ koordynator: JEDYNY właściciel ImportConfiguration.
                             Para BuildConfiguration/ApplyConfiguration to jedyny punkt tłumaczenia
                             „stan UI ⇄ rekord" (§4.8.6) — sekcje, których jeszcze nie ma (Cel,
                             Mapowanie, Behavior), są PRZEPUSZCZANE bez zmian, więc profil z
                             nowszego builda nie zostanie po cichu okrojony przez starszy.
                             Łańcuch przeliczeń §4.7: leniwy, anulowalny (CTS), kolejność
                             wykrywania KODOWANIE→SEPARATOR (separator szuka się w tekście, który
                             ktoś już zdekodował — inaczej byłaby to zgadywanka na zgadywance)
        ImportSourceSectionViewModel.cs  sekcja Źródło i format; NIE posiada konfiguracji, tylko
                             produkuje i czyta swój wycinek. `SuspendChangeNotifications` — bez
                             tego propozycja detektora restartowałaby łańcuch, który ją wywołał
        ImportReadinessViewModel.cs      pasek gotowości: czysta PROJEKCJA Core'owego
                             `ImportReadiness` — zero własnych decyzji. Kod→zdanie w jednym
                             miejscu (reguła #6); severity mapowane na wspólną tablicę
                             `MessageBanner.BrushKeyFor/GeometryKeyFor` (§9.3), więc pasek i
                             banner nie mogą opisać tej samej rzeczy inaczej
    Views/DataImportTabView.axaml(.cs)   pasy A–H z §3.1 (I5 dostarcza A, C, D, E, G, H);
                             code-behind buduje wyłącznie dynamiczne kolumny podglądu
    Assets/Icons/Actions/import.svg      ⭐ nowa ikona: strzałka wchodząca W TABELĘ. Świadomie NIE
                             `Icon.Download` (taca = „pobierz plik na dysk") — ten moduł wkłada
                             wiersze do TABELI, więc glif rymuje się z `Icon.Table`

src/EmberTern.Firebird/          ⭐ I4 — pierwszy kod modułu dotykający bazy
    FirebirdImportErrorMapper.cs ⭐ FbException → ImportErrorKind **z WEKTORA GDS, nigdy z tekstu**.
                             Klasy jednoznaczne rozstrzyga kod WIODĄCY (skan całego wektora
                             myliłby FK z duplikatem — dzielą element 335545072); jedyny kod
                             wieloznaczny (335544321) rozstrzyga się dopiero dalszym elementem.
                             Wektor obcięcia niesie limit i długość jako LICZBY → do raportu
                             wprost, bez parsowania komunikatu. `Classify` jest CZYSTE, więc
                             pinuje się je zmierzonymi wektorami bez serwera
    FirebirdImportWriter.cs  `FbBatchCommand`; `MultiError` = polityka błędów 1:1 (I0 §2.3);
                             `OVERRIDING SYSTEM VALUE` dla identity ALWAYS; `CommandLock`
                             per paczka (chwytany raz — #98/#120). Linia Data, transakcja
                             robocza użytkownika, **auto-begin, NIGDY auto-commit** (reguła #3)
    FirebirdImportTargetReader.cs  cienki adapter: kolumny z istniejącego
                             `FirebirdMetadataReader.ListColumnsAsync` (jedyny właściciel
                             pytania „jakie kolumny ma ta tabela"), a jedyne, co dokłada, to
                             lista aktywnych triggerów BEFORE INSERT — dekodowana
                             współdzielonym `DecodeTriggerHeader`, bo `RDB$TRIGGER_TYPE` jest
                             bitowe i test `type = 1` przegapiłby trigger wieloakcyjny
                             (zmierzone: nasz labowy ma typ **17**)

src/EmberTern.Core/Import/
    ── I3 ──────────────────────────────────────────────────────────────────────────────────────
    ImportPipeline.cs        ⭐ JEDEN import: kroki 1–7 z §4.4. Nie wie, CO czyta (provider) ani
                             CZY pisze (writer) — „Waliduj" to inny argument, nie inny tryb, więc
                             nie ma drugiej ścieżki, która mogłaby się rozjechać.
                             ⭐ Właściciel okna „indeks w paczce → numer wiersza źródłowego" (D9):
                             raport nigdy nie widzi indeksu paczki. Obie polityki błędów, dławiony
                             postęp, anulowanie. NIE kończy transakcji (reguła #3) i NIE tworzy
                             tabeli (linia Ddl, przed przebiegiem — #213)
    DryRunImportWriter.cs    druga produkcyjna implementacja IImportWriter — **funkcja produktu
                             („Waliduj"), nie atrapa testowa**; to ona sprawia, że I1–I3 dają pełną
                             funkcjonalność bez bazy
    Providers/
        DelimitedTextImportProvider.cs   CSV / TXT / **schowek** — jeden provider, trzy pochodzenia
                             tekstu (§1.5). Rozstrzyga tu token NULL (własność *czytania* pola
                             tekstowego); nie konwertuje niczego więcej. Szerokość schematu = NAJSZERSZY
                             rekord próbki, nie nagłówek — kolumna, której nagłówek nie nazwał, i tak
                             musi być mapowalna
    ── I2 ──────────────────────────────────────────────────────────────────────────────────────
    ImportTargetType.cs      ⭐ JEDYNY właściciel pytania „jakiego typu jest ta kolumna docelowa":
                             sformatowany typ z katalogu → SqlValueKind + Size/Scale/BlobSubType +
                             zakres liczby całkowitej. Używa SqlValueKind „w drugą stronę" (§4.6);
                             zbiór typów NIEOBSŁUGIWANYCH jest celowo IDENTYCZNY jak po stronie
                             eksportu — czego eksport nie umie zapisać, tego import nie wypełnia
    ImportDiagnostics.cs     ImportSeverity · ImportSection · ImportDiagnosticCode (IMP0001–IMP0027)
                             + ImportDiagnostic. Kody, nigdy teksty (reguła #6). JEDEN katalog dla
                             planera i dla gotowości — dwa katalogi mogłyby się rozjechać
    ImportValueConverter.cs  ⭐ ścisła konwersja (§0.1): wartość pewna albo odmowa z powodem.
                             + ImportValueResult (readonly struct — jedna waluta kroków 3 i 4)
    ImportRowValidator.cs    NOT NULL · długość (+ opcjonalne przycięcie) · precyzja/skala ·
                             ⭐ ImportCharsetGuard — reprezentowalność w charsecie POŁĄCZENIA
                                z EncoderExceptionFallback (R1/REK-2, warunek §0)
    ImportMappingPlanner.cs  auto-mapowanie po nazwie · reguła zachowania dowodliwego (§4.7) ·
                             reguła jedynej pary · Diagnose() · Project() (krok 2 pipeline'u)
    ImportReadiness.cs       ⭐ czysta funkcja gotowości (§3.2) + ReadinessItem/Input/Report
    ── I1 ──────────────────────────────────────────────────────────────────────────────────────
    ImportEnums.cs           ImportSourceKind · ImportMode · ImportTransactionMode · ImportErrorPolicy
                             ImportTargetKind · MappingOrigin · DateFieldOrder · LineEndingMode
                             ImportErrorKind  ← kindy klienckie i serwerowe, z komentarzem, dlaczego
                                                niektóre klasy błędów są NIEROZRÓŻNIALNE (I0 §2.6);
                                                I2 dołożył 3 kindy klienckie (niżej)
    ImportOptions.cs         DelimitedOptions · SpreadsheetOptions · ImportCultureOptions
                                                (+ BuildNumberFormat / IsTrueToken / IsFalseToken)
    ImportConfiguration.cs   ⭐ ImportConfiguration + SourceDescriptor · TargetDescriptor
                             ImportColumnDefinition · ColumnMapping · ImportBehaviorOptions
    ImportModels.cs          SourceField · SourceSchema · RawRecord · ImportTarget · ImportRow
                             ImportRowError · ImportProgress · ImportBatchItemResult
                             ImportWriteSummary · ImportOutcome
    ImportContracts.cs       IImportSource · IImportProvider · IImportWriter (+ ImportProviderCapabilities)
    ImportProfile.cs         ImportProfile (encja trwała)
    ImportProfileStore.cs    fasada sekcji settings.dat — GetLastUsed / SaveLastUsed / ClearLastUsed
    Providers/
        DelimitedTextReader.cs   RFC 4180, strumieniowy (ReadAll / ReadSample)
        DelimiterDetector.cs     DelimiterProposal + dowody liczbowe
        EncodingDetector.cs      EncodingProposal + EncodingDetectionBasis + ByteOrderMarkLength
        FileImportSource.cs      IImportSource nad plikiem (+ ReadDetectionSample)
        TextImportSource.cs      IImportSource nad string (schowek)
```

Zmiany w plikach współdzielonych — **obie addytywne, obie zaakceptowane**:
- `Core/Connections/CharsetCatalog.Resolve` → rozpoznaje `UTF16LE` / `UTF16BE`. Jest już **jedynym**
  właścicielem odwzorowania „nazwa charsetu → `Encoding`", więc drugie takie odwzorowanie byłoby rozjazdem.
  Nazwy **nie** weszły do `Supported` (to lista charsetów POŁĄCZENIA, a Firebird takich nie ma).
- `Core/Settings/UserSettings` → `List<Import.ImportProfile> ImportProfiles`. **Wersja schematu
  settings.dat celowo NIE podbita** (podbicie uruchamia ochronę przed downgrade'em i starszy build
  odmówiłby odczytu całego pliku).

### Testy I6 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `DataImportTabVmTests` (I6, +15) | ⭐ **siatka pokazuje KAŻDĄ kolumnę tabeli**, także tę, której nigdy nie da się zapisać — z powodem, nie przez pominięcie (brakujący wiersz to pytanie, którego użytkownik nie może nawet zadać) · auto-dopasowanie po nazwie **pochodzi z planera**, VM je tylko rysuje · ⭐ **identity ALWAYS zablokowane do jawnego odblokowania** (R10) · ⭐ **reguła jedynej pary sięga też po kolumnę identity — i to jest zamierzone**: Core uznaje ją za mapowalną i podnosi `IMP0007`, więc nic nie dzieje się po cichu · ręczna zmiana **kasuje pochodzenie automatyczne** i dociera do rekordu · „nie importuj" zapisuje się jako **pominięcie**, nie jako brak · ⭐ **inna tabela nie dziedziczy mapowania** · ⭐⭐ **wyczyszczenie CELU czyści siatkę, ale NIE rekord** — inaczej przywrócony profil traciłby parowanie tylko dlatego, że celu jeszcze nie odczytano · lista pól nieużywanych (fixture ma **dwa** zapasowe pola, żeby reguła jedynej pary nie zaliczyła testu przypadkiem) · cel i „opróżnij tabelę" docierają do JEDNEGO rekordu · ⭐ **konfiguracja z NOWĄ tabelą jest przepuszczana bez zmian** (to etap I8) · gotowość widzi wybrany cel · filtr „tylko niezmapowane" zmienia widok, nigdy rekordu |

### Testy I5 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `DataImportTabVmTests` | ⭐⭐ **`Configuration_SurvivesABuildApplyRoundTrip`** — obietnica §4.8.6 na poziomie App: ustawienie dodane prosto do VM-a sekcji byłoby niewidoczne dla profilu, a defekt wyszedłby dopiero w I11 jako „przebuduj powierzchnię"; ten test wywala się pierwszy · ⭐ **sekcje, których jeszcze nie ma, są przepuszczane** (starszy build nie okrada profilu nowszego) · ⭐ **wiersz odstający od WIĘKSZOŚCI jest oznaczany, nie od najszerszego** · detekcja wpisuje wartość ZADEKLAROWANĄ i publikuje dowód; wyłączona — nie rusza niczego · brak pliku i arkusz to **odmowa z powodem**, nie wyjątek i nie cisza · fakty środowiska czytane jako DELEGATY (pasek pokazuje stan teraz, nie z chwili otwarcia) · ⭐ **każdy kod diagnostyczny ma zdanie** (inaczej słownik Core wycieka do użytkownika) · każdy chip ma rozwiązywalny klucz pędzla i geometrii (#250) |

⚠ **Reguła QA projektu:** build 0/0, 5559 zielonych i czysty start aplikacji **nie wystarczają**, żeby nazwać
etap UI zrobionym — i I5 jest tego dowodem. Przegląd wzrokowy **odbył się 2026-07-26** i znalazł pięć rzeczy,
których żaden z 296 testów importu nie mógł znaleźć, bo wszystkie dotyczą **proporcji i przestrzeni**, a nie
stanu: sekcja parametrów zjada pion, dolny panel nie ma splittera, nagłówek pasa A jest zbędny, globalne
kontrolki są za wysokie, a na Full HD formularz nie mieści się w pionie. **Zapisane jako U1–U5 w §3.8.**

### Testy I4 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `ImportErrorMapperTests` | **każdy wektor to wektor ZMIERZONY** na żywym FB5, przepisany dosłownie — więc gdy przyszły Firebird zmieni kod, test się wywala i ktoś mierzy ponownie, zamiast po cichu wysłać zły raport; ⭐ trzy klasy dzielące kod wiodący `335544321` rozdzielają się poprawnie; limit i długość czytane **po wartości, nie po pozycji** (w zmierzonym wektorze między dyskryminatorem a liczbami stoi jeszcze jeden kod GDS); ⭐ **skan całego wektora myliłby FK z duplikatem** — dlatego decyduje kod wiodący; ⭐⭐ **samodzielny `UNIQUE INDEX` wiedzie innym kodem niż ograniczenie** (znalezione przebiegiem na żywo, nie w I0) |
| `ImportFirebirdWriterTests` | ⭐ `OVERRIDING SYSTEM VALUE` emitowane **tylko** dla zmapowanej kolumny identity ALWAYS (brak klauzuli = śmierć na pierwszym wierszu, klauzula zbędna = równie źle); cytowanie identyfikatorów z podwojeniem cudzysłowu; ⭐ `MultiError` ↔ `ImportErrorPolicy` 1:1 — odwrócenie tego jest niewidoczne aż do momentu, gdy zmienia to, co przebieg **robi** |
| `tools/probes/DataImportProbe` (żywy FB5) | **20/20 ALL PASS.** 7 klas błędów serwera z właściwym rodzajem **i właściwym numerem wiersza ŹRÓDŁOWEGO** (zły wiersz to zawsze 3. wiersz danych, nigdy 1. — żeby przesunięcie o jeden ani wyciek indeksu paczki nie przeszły przypadkiem) · 3 przypadki dowodzące, że **strażniki klienckie strzelają PIERWSZE** · ⭐ numer wiersza przeżywa **granicę paczki** · 10 000 wierszy zgodnych z `SELECT COUNT(*)` · ⭐ writer **nigdy nie zatwierdza** (Rollback usuwa wszystko) · ⭐ trigger wieloakcyjny znaleziony · charset odmawia po stronie klienta |

⚠ **Trzy klasy serwerowe wymagały triggera, żeby dało się je w ogóle wywołać na żywo** (`IMP_SRV`): klient
waliduje NOT NULL, długość i zakres **przed** round tripem — co jest poprawne i zgodne z §0, ale znaczy, że
bez triggera produkującego te błędy *wewnątrz silnika* gałęzie rozróżniania wektora nie miałyby jak zostać
sprawdzone przeciwko prawdziwemu serwerowi.

### Testy I3 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `ImportPipelineTests` | ⭐⭐ **`BatchFailure_IsReportedAgainstTheSourceRow_NotTheBatchIndex`** — writer wywala pozycję 1 **drugiej** paczki, co przy nagłówku i paczce po 2 odpowiada wierszowi źródłowemu **5**; pipeline przepuszczający indeks powiedziałby „wiersz 1" i wysłał użytkownika pod zły wiersz pliku (fixture celowo trzyma obie liczby różne, żeby test nie przeszedł przypadkiem) · ⭐ **dry-run i prawdziwy writer dają identyczny wynik** (inaczej „Waliduj mówi OK" przestaje coś znaczyć) · obcięty wynik paczki (`MultiError=false`) **nie zmyśla werdyktów dla wierszy, których nie spróbowano** · ⭐ **wartość przycięta to OSTRZEŻENIE z oryginałem, nie błąd** i nie zawyża `RowsFailed` · ⭐ **anulowanie nie porzuca wierszy już przyjętych** (ogonowy flush na nieanulowanym tokenie) · limit listy błędów przy dokładnych licznikach · R1 przez cały pipeline · odmowa startu przy pustym mapowaniu i przy mapowaniu na nieistniejącą kolumnę |
| `ImportDelimitedProviderTests` | schemat z nagłówka i bez (etykiety pozycyjne + `HasRealName=false`); ⭐ **szerokość z najszerszego rekordu, nie z nagłówka**; nagłówek pomijany **oknem wierszy, nie przypadkiem szczególnym** (linie bannerowe nad nagłówkiem działają); pole wielolinijkowe to JEDEN rekord, więc numeracja raportu zostaje numeracją pliku; token NULL domyślny i zadeklarowany (bez rozróżniania wielkości liter); strumieniowość i anulowanie; **brak zmyślonej liczby wierszy** |

### Testy I2 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `ImportValueConverterTests` | rozpoznanie każdego typu z katalogu + **zbiór typów nieobsługiwanych**; NULL i pole puste; każda szerokość liczby całkowitej i jej zakres; ⭐ **`"1.5"` przy przecinku dziesiętnym to BŁĄD, nie 1,5 i nie 15**; ⭐ **`03.04.2026` czytane wyłącznie w zadeklarowanej kolejności pól** (DMY → 3 kwietnia, MDY → 4 marca); data z godziną do kolumny `DATE` odrzucona; wartości natywne z arkusza; wartość surowa zachowana dla raportu |
| `ImportRowValidatorTests` | ⭐⭐ **`Guard_WithoutTheExceptionFallback_WouldSilentlyCorrupt`** — odtwarza samą korupcję (`Ж` → `?`), więc „uproszczenie" fallbacku wywala test, który mówi, co właśnie włączono z powrotem; ⭐ **charset POŁĄCZENIA decyduje, nie kolumny**; polski tekst w WIN1250 przechodzi (inaczej strażnik zostałby wyłączony w jeden dzień); ⭐ **NOT NULL z DEFAULT-em i tak odrzuca NULL w kolumnie ZMAPOWANEJ**; ⭐ **`1,50` w `NUMERIC(15,1)` NIE jest utratą precyzji** (porównanie po wartości, nie po zapisanej skali); przycinanie tylko na życzenie i zawsze z oryginałem |
| `ImportMappingPlannerTests` | normalizacja nazwy (spacja ≡ podkreślenie), ale **bez zdejmowania diakrytyków**; ⭐ **nazwa niejednoznaczna nie łączy NICZEGO**; ⭐ **reguła jedynej pary nie działa przy dwóch kandydatach z każdej strony**; ⭐ **R16: przestawienie kolumn w pliku — mapowanie idzie za NAZWĄ, nie za pozycją**; pominięcie przeżywa ponowny odczyt; kolumna `COMPUTED`/o typie nieobsługiwanym nigdy nie zmapowana, ale **widoczna z powodem**; `NOT NULL` z DEFAULT-em nie blokuje, gdy jest niezmapowana; `Project` nie dopełnia rekordu poszarpanego |
| `ImportReadinessTests` | pełna macierz blokujące vs ostrzegawcze; ⭐ **wszystkie braki raportowane naraz** (przewaga nad przyciskiem „Dalej"); ⭐ **`NewTableWillBeCommitted` ostrzega, ale NIE blokuje** (§0.5 / #213); ⭐ **wnioski o mapowaniu pochodzą z planera, nie z drugiej analizy**; projekcja na sekcje (`SeverityFor` / `IsSectionRunnable`); każdy bloker wskazuje sekcję |

### Testy I1 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `ImportDelimitedReaderTests` (44) | cudzysłowy, podwojone cudzysłowy, separator i łamanie linii w wartości, CR/LF/CRLF + tryby jawne, puste pola, rekordy poszarpane (bez dopełniania), przycinanie tylko poza cudzysłowami, **pominięty pusty wiersz nie przesuwa numeru rekordu** |
| `ImportConfigurationRoundTripTests` (11) | ⭐ **refleksyjny strażnik**: każda zapisywalna właściwość (rekurencyjnie) musi być ustawiona na wartość niedomyślną **i** przetrwać prawdziwy serializator. Porównanie jest **strukturalne**, bo równość rekordów porównuje `IReadOnlyList` przez referencję i przepuściłaby round trip, który zgubił wszystkie elementy |
| `ImportDetectorTests` (20) | propozycje separatora i kodowania **wraz z dowodami**; „plik jest czystym ASCII i nie rozróżnia kodowań" jako jawny wynik |
| `ImportProfileStoreTests` (11) | round trip przez settings.dat, zakres per połączenie, brak klobrowania innych sekcji, **odrzucenie konfiguracji z przyszłej wersji w całości**, brak podbicia wersji schematu |

⚠ **Dwa wyjątki w strażniku refleksyjnym**, oba samo-unieważniające się: `Version` (znacznik schematu —
fixture udający przyszłą wersję byłby nonsensem, pinowane osobno w `ImportProfileStoreTests`) oraz `Mode`
(`ImportMode` ma w v1 jeden element, więc nie istnieje wartość niedomyślna) — ten drugi jest **warunkowy**
i osobny test `ImportMode_StillHasOneMember_OrTheFixtureMustCoverIt` wywala się w dniu, w którym enum
urośnie. **Wyjątku nie wolno rozszerzać na nową właściwość** — jeśli strażnik protestuje, poprawką jest
ustawienie tej właściwości w `Fully()`.

### Odstępstwa od dokumentu przyjęte w trakcie implementacji

| Odstępstwo | Powód | Status |
|---|---|---|
| `TrimWhitespace` **tylko** w `DelimitedOptions`, mimo że szkic v2 §4.8.2 wymieniał je też w `ImportBehaviorOptions` | przycinanie białych znaków jest własnością *czytania pola tekstowego*, a sekcja Format pokazuje je tam; dwa domy dla jednej decyzji to rozdwojenie, przed którym broni zasada jednego właściciela | ✅ zaakceptowane 2026-07-26; **nie dodawać drugiego pola tylko po to, by zgadzało się ze szkicem — dokument ma odzwierciedlać poprawioną architekturę, nie odwrotnie** |
| `IImportProvider` dostaje całą `ImportConfiguration`, nie wybrany obiekt opcji | konfiguracja jest jedyną reprezentacją tego, o co poprosił użytkownik (§4.8.1); provider czyta tylko swój blok | ✅ w ramach swobody „sygnatury poglądowe" z §4.3 |
| `DelimitedTextImportProvider` **nie** powstał w I1 | wiersz I1 w §6 go nie wymienia; pierwszym etapem, który go potrzebuje, jest I3 (pipeline end-to-end na `TextImportSource`) | ✅ **dostarczony w I3.** Reguła #2 jest teraz spełniona dla dwóch z trzech portów: `IImportSource` (plik + tekst) i `IImportWriter` (dry-run + Firebird w I4). `IImportProvider` ma jedną implementację do czasu `XlsxImportProvider` (I9) — przejściowo, zgodnie z §4.3 |
| **I3: `ImportOutcome` +`Warnings` / +`WarningsTruncated`** (właściwości `init`, nie parametry pozycyjne ⇒ zero zmian w istniejących wywołaniach) | §0.2 wymaga, żeby **każdy skrócony wiersz trafił do raportu z oryginalną wartością**, a taki wiersz nie jest ani błędem (wszedł), ani ciszą (dane przepadły). Wrzucenie go do `Errors` zawyżałoby `RowsFailed` o wiersze, którym się udało — czyli raport by kłamał (§0.6) | ✅ addytywne; zgłoszone jako as-built 2026-07-26. Kind pozostaje `ValueTooLong` — przyczyna jest ta sama, a to, **na której liście** wpis leży, mówi, co z nią zrobiono (odmowa vs skrócenie); nowy kind byłby czwartym w dwa etapy bez zysku informacyjnego |
| **I3: `ImportPipeline.RunAsync` bierze `target` i `connectionEncoding`** ponad szkic z §4.4 | `ImportTarget` to **fakt odczytany ze świata**, więc z definicji nie leży w konfiguracji (§4.8.2), a bez charsetu połączenia walidacja R1 nie ma czym się posłużyć. §4.3 nazywa sygnatury „poglądowymi" | ✅ w ramach swobody z §4.3 |
| **I3: `RunAsync` jest `static`** | pipeline nie trzyma stanu między przebiegami (cały stan przebiegu jest lokalny), a §4.3 wymienia go wśród „zwykłych klas", nie portów. Forma wywołania zgadza się ze szkicem `ImportPipeline.RunAsync(...)` z §4.4 | ✅ |
| **I3: `CreatedTable` zwracane jako `null`** | tworzenie tabeli dzieje się na **linii Ddl, przed** przebiegiem (§4.5 / #213), więc pipeline nie ma o nim wiedzy. Koordynator uzupełnia je przez `outcome with { CreatedTable = … }` | ℹ️ do podłączenia w I7/I8 |
| **I2: `ImportErrorKind` +3 kindy klienckie** — `ValueOutOfRange`, `PrecisionWouldBeLost`, `UnsupportedTargetType` | DoD etapu I2 brzmi „**zero cichych konwersji**", a bez nich nie da się go spełnić: liczba poprawna, lecz za duża dla kolumny, zaokrąglenie `1,555` → `1,56` w `NUMERIC(15,2)` i kolumna typu, którego nie umiemy zapisać, nie miały czym być zaraportowane. Zgłoszenie ich jako `NotAnInteger` byłoby **przekłamaniem powodu**. Addytywne: żaden port, przepływ, model ani decyzja się nie zmieniają | ✅ zaakceptowane 2026-07-26 (zgłoszone jako as-built, nie jako zmiana projektu). **Świadomie NIE dodano opcji „zaokrąglij mimo to"** — to byłaby decyzja projektowa, a §0.1 domyślnie nakazuje odmowę |
| **I2: `ImportTargetType` jako osobna klasa**, choć §4.2 jej nie wymienia | §4.6 nakazuje użyć `SqlValueKind` „w drugą stronę" i nie tworzyć drugiego modelu typu. Pytanie „ile znaków ma `VARCHAR(20)`" zadają **cztery** komponenty (konwerter, walidator, planer, gotowość); cztery niezależne wyprowadzenia to dokładnie sposób, w jaki kontrola długości i ostrzeżenie o długości zaczynają mówić użytkownikowi co innego | ✅ zaakceptowane 2026-07-26 — to realizacja „Single Source of Truth" z §1.4, nie nowy byt architektoniczny |
| **I2: `ImportRowValidator` dostaje `ImportBehaviorOptions` + `Encoding`**, a nie tylko `(value, ColumnSpec)` | przycinanie (`TrimTooLongValues`) i charset połączenia to **wejścia** walidacji wymienione wprost w §4.4 krok 4; §4.3 nazywa sygnatury „poglądowymi" | ✅ w ramach swobody z §4.3 |
| **I6: liczba rekordów tabeli NIE jest pokazywana w linii faktów**, mimo że szkic §3.4 ją wymienia | to `SELECT COUNT(*)` przy **każdej** zmianie celu — na dużej tabeli sekundy, na produkcyjnej bazie z 2388 tabelami koszt jest realny. Decyzja, której ta liczba służy („zaraz skasujesz N wierszy"), zapada **przy starcie importu**, więc liczbę odczytamy raz, w I7, tam gdzie jest potrzebna | ✅ zgłoszone jako as-built 2026-07-26; **do wykonania w I7** wraz z potwierdzeniem opróżnienia |
| **I6: wariant „Nowa tabela" nie jest pokazany nawet jako wyłączony przełącznik** | opcja, która wygląda na wybór i prowadzi donikąd, to kłamstwo, którego pasek gotowości nie skoryguje — ta sama zasada, dla której I5 nie zbudował paska poleceń. `TargetDescriptor` z nową tabelą jest mimo to **przepuszczany bez zmian** (pinowane testem) | ✅ dochodzi w I8 |
| **I6: reguła jedynej pary może sparować kolumnę identity `GENERATED ALWAYS`** | pozorna sprzeczność z §3.5 („tylko po jawnym odblokowaniu") jest rozstrzygnięta **w samym Core**: `IsMappable` nie wyklucza identity, a `Diagnose` podnosi wtedy `IMP0007` — *„akcent, nie usterka, ale nigdy po cichu"*. Blokada w UI dotyczy więc sięgania po tę kolumnę **ręcznie**; gdy sparował ją planer, wiersz pokazuje się odblokowany, oznaczony jako „założone", a writer emituje `OVERRIDING SYSTEM VALUE` (I4). Nic nie dzieje się milcząco | ✅ zachowanie zgodne z zamrożonym Core; **żadnej zmiany w Core** — udokumentowane i pinowane testem |
| **I2: `ImportSeverity` / `ImportSection` żyją w Core** | pasek gotowości ma używać **tej samej** mapy `Severity` → pędzel co `MessageBanner` (§9.3), ale `MessageSeverity` mieszka w `App.Controls`, a Core nie może zależeć od App (reguła #1). Core zwraca więc własną trójwartościową severity, a App mapuje ją **jednym przejściem** na `MessageSeverity` i dalej już przez `BrushKeyFor`/`GeometryKeyFor` — druga mapa pędzli nie powstaje | ✅ zgodne z §9.3; do wykonania po stronie App w I5 |

### Stan dokumentów projektowych

| Dokument | Rola | Stan |
|---|---|---|
| `docs/design/data-import.md` (ten) | jedyna architektoniczna prawda modułu | 🔒 **ZAMROŻONY** (v3). Zmiany tylko „w miejscu" i tylko o stan faktyczny |
| `docs/design/data-import-i0-findings.md` | archiwum dowodowe pomiarów I0 | zamknięty, 8 rekomendacji zaakceptowanych |
| `tools/probes/DataImportWriteProbe` | sonda ścieżki zapisu (I0, surowy sterownik) | ✅ **I4 zamknięty — do usunięcia.** Jej rolę przejęła `DataImportProbe`, która sprawdza to samo, ale **kodem produkcyjnym** |
| `tools/probes/DataImportProbe` | ⭐ weryfikacja na żywo **kodu produkcyjnego** (I4) | **trzymać** — to jest regresyjny dowód warstwy Firebirda; uruchamiać po każdej zmianie writera lub mappera |
| `tools/probes/DataImportXlsxProbe` | sonda odczytu `.xlsx` | **trzymać do I9**, potem usunąć |
| `docs/history/` + `docs/gotchas.md` + CLAUDE.md (pełny wpis) | narracja i katalog gotch | **planowo w I12** (wiersz „Domknięcie" w §6) |

---

## §0. Prawo nadrzędne modułu — nigdy nie zgub i nigdy nie przekłamaj danych

Reguła architektoniczna #11 EmberTerna („nigdy nie trać informacji / nigdy nie psuj kodu ani metadanych
użytkownika") w module importu ma **konkretne, wiążące konsekwencje**. To nie jest ozdobnik — to jest
kryterium odbioru każdego etapu:

1. **Żadnej cichej konwersji.** Wartość, której moduł nie potrafi przekonwertować z **pewnością**, jest
   **błędem wiersza**, nigdy zgadywanką. `"1,5"` przy separatorze dziesiętnym `.` to błąd, a nie `15`.
2. **Żadnego cichego obcięcia.** Tekst dłuższy niż `VARCHAR(n)` to domyślnie błąd. Obcinanie
   (odpowiednik IBExpertowego *„Przytnij wartości łańcuchowe jeśli są za długie"*) istnieje **wyłącznie**
   jako jawnie zaznaczona opcja, opisana skutkiem („wartości zostaną skrócone — dane zostaną utracone"),
   i każdy skrócony wiersz trafia do raportu jako **ostrzeżenie z oryginalną wartością**.
3. **Żadnego zgadywania typu przy tworzeniu tabeli.** Wnioskowanie typów jest **zachowawcze**: przy
   jakiejkolwiek niejednoznaczności wygrywa `VARCHAR` (bezstratny nośnik tekstu), nigdy „prawdopodobnie
   liczba". Wynik wnioskowania jest **pokazany i edytowalny** przed wykonaniem DDL.
4. **Kultura jest deklarowana, nie wykrywana po cichu.** Separator dziesiętny, format daty i kodowanie są
   jawnymi ustawieniami z wartościami domyślnymi. Auto-detekcja **proponuje** (z pokazaną podstawą),
   nigdy nie decyduje milcząco.
5. **Transakcja mówi prawdę.** Użytkownik przed startem wie dokładnie, co zostanie zatwierdzone, co
   zostanie otwarte i czego Rollback **nie cofnie** (utworzona tabela, wartości generatorów, skutki
   triggerów, zatwierdzone paczki w trybie wsadowym).
6. **Raport nie kłamie.** „Zaimportowano N rekordów" oznacza N rekordów, które serwer przyjął. Jeżeli
   transakcja pozostaje otwarta, raport mówi *„N wierszy wstawionych — transakcja otwarta, zatwierdź lub
   wycofaj"*, a nie *„import zakończony powodzeniem"*.
7. **Wczytany profil nigdy nie przemilcza zmiany.** Jeżeli konfiguracja z profilu przestała pasować do
   źródła albo do tabeli docelowej, moduł **oddaje decyzję użytkownikowi** i mówi, czego nie potrafi
   odtworzyć (§4.8.5). Profil nie może być cichym sprawcą złego importu.

Każde miejsce, w którym moduł musiałby zgadywać, ma dokładnie dwa dozwolone wyjścia: **zapytać
użytkownika** albo **odmówić z powodem**.

---

## 1. Ogólna koncepcja modułu

### 1.1. Czym moduł jest

**Data Import** to kolejna zakładka narzędziowa w przestrzeni roboczej EmberTerna — dokładny rówieśnik
**Script Executora**: otwierana z toolbara głównego okna (decyzja **D6**), near-singleton na połączenie,
nietrwała (nie odtwarzana po restarcie). Działa **zawsze na aktualnie połączonej bazie** — nie ma pola
„baza docelowa" (IBExpert je ma, bo jest wielobazowy w jednym oknie; EmberTern ma jedno aktywne
połączenie i dodanie takiego pola byłoby kłamstwem interfejsu).

**Moduł jest jedną powierzchnią roboczą**, nie kreatorem: źródło, ustawienia, cel, mapowanie i podgląd
są dostępne jednocześnie, a gotowość do importu pokazuje **pasek gotowości** (pre-flight) zamiast
przycisków „Dalej".

### 1.2. Dlaczego nie kreator (decyzja ratyfikowana 2026-07-26)

Kreator optymalizuje **pierwszy** przebieg kosztem **N-tego**, a odbiorcą EmberTerna jest programista
lub administrator, który ten sam import wykonuje wielokrotnie. Trzy niezależne argumenty prowadzą do
tego samego wniosku:

1. **Profil użytkownika** — powtarzalny import ma kosztować jedno naciśnięcie `F5`, nie sześć kliknięć
   „Dalej". Przy jednej powierzchni „czy na pewno tak samo jak ostatnio" jest odpowiedzią wzrokową,
   nie nawigacyjną.
2. **Spójność z aplikacją** — EmberTern nie ma ani jednego kreatora. Ma zakładki narzędziowe
   (Script Executor, Debugger: pasek narzędzi + główna powierzchnia + dolny `TabControl`) i edytory
   wielozakładkowe. Istnieje już ratyfikowany precedens przeciw ceremonii: D15.3 Seam C wprowadził
   **„no-decision fast path"** — procedura bez parametrów i z czystym pre-flightem *pomija panel
   uruchomienia*.
3. **Koszt implementacji** — jedna powierzchnia to **mniej** kodu: znikają osobne widoki kroków,
   nawigacja, reguły osiągalności, bramkowanie „Dalej" i unieważnianie kroków w przód. Zostaje jedna
   reaktywna powierzchnia i jedna czysta funkcja gotowości.

Z kreatora zachowujemy dokładnie dwie rzeczy, bo były jego realną wartością:
- **kolejność logiczną** — źródło → format → cel → mapowanie → podgląd, czytaną z góry na dół;
- **bramkę** — w postaci paska gotowości, który jest **lepszy** od przycisku „Dalej", bo pokazuje
  wszystkie braki naraz, a nie pierwszy napotkany.

### 1.3. Czym moduł nie jest

- Nie jest kopią IBExperta. Nie odtwarzamy gęstego, czterozakładkowego okna, w którym „Podgląd danych"
  pokazuje surowe komórki źródła, a użytkownik dowiaduje się o niezgodności typów dopiero po
  `Start import`.
- Nie jest silnikiem ETL. Transformacje, kolumny wyliczane i filtrowanie są **przyszłymi rozszerzeniami**
  z zaprojektowanymi punktami wpięcia (§9.5), nie zakresem v1.
- Nie jest systemem wtyczek (reguła #10). „Provider" to wewnętrzna implementacja interfejsu w tym samym
  solution, nie ładowany dynamicznie plugin.

### 1.4. Trzy filary architektoniczne

| Filar | Realizacja |
|---|---|
| **Jeden pipeline niezależny od źródła** | Provider produkuje `SourceSchema` + strumień `RawRecord`. Od tego miejsca **wszystko jest wspólne**: mapowanie → konwersja → walidacja → zapis → raport. CSV, XLSX i schowek różnią się **wyłącznie** implementacją czytnika. |
| **Core First** | Cały pipeline, modele, wnioskowanie typów, konwersja wartości, planowanie mapowania, **model konfiguracji** i **funkcja gotowości** są czystym Core (zero Avalonia, zero `FirebirdSql`) — testowalne bez bazy i bez UI. Firebird dostaje jedną odpowiedzialność: wykonać `INSERT` i przetłumaczyć błąd serwera. |
| **Single Source of Truth** | Zero nowego generatora DDL (`DdlGenerator.BuildCreateTable`), zero drugiego modelu kolumny (`ColumnSpec`), zero drugiej definicji pola (`FieldDefinition`/`TableSpec`), zero drugiej ścieżki eksportu raportu (framework `EmberTern.Core.Export`), zero drugiego mechanizmu transakcji (`TransactionService` + linia Data / linia Ddl). **I — krytycznie dla profili — jedna reprezentacja konfiguracji: `ImportConfiguration` (§4.8).** |

### 1.5. Odwzorowanie źródeł na providerów

| Źródło UI | Provider | Projekt | Uwaga |
|---|---|---|---|
| Clipboard | `DelimitedTextImportProvider` | Core | Schowek **nie jest osobnym parserem** — to inne pochodzenie tekstu. App czyta schowek (typy Avalonia zostają w App), Core dostaje `string`. |
| TXT | `DelimitedTextImportProvider` | Core | Ten sam provider, inne domyślne opcje (separator TAB). |
| CSV | `DelimitedTextImportProvider` | Core | Domyślnie `;` (locale PL) z auto-detekcją. |
| XLSX | `XlsxImportProvider` | **`EmberTern.Office`** (D1 zatwierdzone) | Czytanie SAX-owe przez obecny `DocumentFormat.OpenXml`. |
| XLS (BIFF8) | `XlsImportProvider` | `EmberTern.Office` | **Poza MVP** (D2 zatwierdzone) — etap I10. |

---

## 2. Proponowany workflow użytkownika

### 2.1. Model interakcji: **jedna powierzchnia, stan zawsze żywy**

Nie ma kroków, nie ma nawigacji i nie ma trybów. Jest **jeden stan konfiguracji**
(`ImportConfiguration`, §4.8) i **jedna funkcja gotowości** (`ImportReadiness`), przeliczana po każdej
zmianie. Wszystko, co użytkownik widzi, jest projekcją tych dwóch rzeczy.

```
Toolbar „Import danych"  →  zakładka „Import danych"
                              │
        ┌─────────────────────┴─────────────────────┐
        │  (opcjonalnie) przywrócona ostatnia       │  ← §4.8.4: MVP pamięta ostatnią konfigurację
        │  konfiguracja dla tego połączenia          │     (nazwane profile = to samo UI później)
        └─────────────────────┬─────────────────────┘
                              │
   ╔══════════════════════════▼══════════════════════════════════════════╗
   ║  Jedna powierzchnia. Kolejność czytania = kolejność zależności,     ║
   ║  ale każda sekcja jest dostępna w każdej chwili.                    ║
   ║                                                                     ║
   ║   Źródło i format  ──▶  Cel  ──▶  Mapowanie  ──▶  Podgląd           ║
   ║        ▲                  ▲            ▲              │             ║
   ║        └──────── każda zmiana przelicza wszystko w prawo ───────────╢
   ║                                                                     ║
   ║   Pasek gotowości mówi, czego brakuje. Nic nie jest zablokowane     ║
   ║   poza samym uruchomieniem importu.                                 ║
   ╚══════════════════════════┬══════════════════════════════════════════╝
                              │
              [Waliduj]  (dry-run: pełny przebieg bez zapisu)
                              │
              [Importuj F5]  →  postęp + anulowanie  →  RAPORT
                              │
              Commit / Rollback   (tryb Manual — domyślny, D3)
```

### 2.2. Zasady zachowania powierzchni

1. **Sekcja kompletna zwija się do jednej linii podsumowania.** `▸ Fantomy.xlsx · Arkusz1 · w. 2–1241 ·
   WIN1250 · separator ";"  [Zmień]`. Ekspert widzi całą konfigurację na jednym ekranie; nowy użytkownik
   widzi rozwiniętą pierwszą niekompletną sekcję i czyta z góry na dół — czyli w tej samej kolejności,
   którą narzucał kreator, tylko bez bramek.
2. **Pierwsza niekompletna sekcja jest automatycznie rozwinięta.** Zwijanie/rozwijanie ręczne zawsze
   wygrywa nad automatem (automat nie walczy z użytkownikiem).
3. **Zmiana wcześniejszej decyzji nie kasuje pracy dalszej.** Obowiązuje **reguła zachowania
   dowodliwego** (§4.7).
4. **Podgląd jest ciągły, nie końcowy.** Zmiana separatora dziesiętnego natychmiast przelicza wartości w
   podglądzie. To jedyny układ, w którym „podgląd po konwersji" jest narzędziem diagnostycznym, a nie
   ekranem kontrolnym.
5. **Jedyna rzecz, która jest zablokowana, to uruchomienie importu** — i wyłącznie wtedy, gdy pasek
   gotowości ma pozycję blokującą, **zawsze z podanym powodem i skrótem do winnej sekcji**.

### 2.3. Ścieżki alternatywne

| Sytuacja | Zachowanie |
|---|---|
| Otwarta transakcja robocza (np. z SQL Editora) | Pasek gotowości pokazuje pozycję **blokującą** „transakcja robocza jest otwarta — zatwierdź lub wycofaj", identycznie jak `ResolveRunBlock` Script Executora. Cała konfiguracja i podgląd działają normalnie (nic nie piszą). |
| Cel = nowa tabela | Sekcja **Cel** pokazuje ostrzeżenie: *„Tabela X zostanie utworzona i **zatwierdzona** przed wstawieniem danych. Wycofanie importu jej nie usunie."* + opcjonalny checkbox „usuń tabelę, jeśli import się nie powiedzie". |
| Import anulowany w trakcie | Wstawione wiersze **zostają w otwartej transakcji**; użytkownik decyduje Commit/Rollback. Raport mówi wprost: *„Anulowano po N wierszach — transakcja otwarta"*. |
| Błędne wiersze | Zgodnie z polityką: `StopOnFirstError` (domyślnie, D4) albo `SkipInvalidRows`. W obu przypadkach raport zawiera listę błędów z numerem wiersza źródłowego. |
| Zamknięcie zakładki w trakcie importu | Potwierdzenie („trwa import — anulować?"), anulowanie, potem zamknięcie. Skonfigurowana-ale-nieuruchomiona powierzchnia **nie jest** „niezapisaną pracą" (nie ma czego zapisać do bazy, a konfiguracja jest zachowywana jako ostatnio użyta) — zakładka zamyka się bez pytania. |
| Plik zniknął / zmienił kształt między konfiguracją a importem | Provider zgłasza to jako pozycję blokującą w pasku gotowości przy najbliższym przeliczeniu; przy starcie importu następuje ponowne odczytanie schematu i porównanie (§4.8.5). |

---

## 3. Makieta powierzchni roboczej (opisowa)

Cała funkcja to **jedna zakładka**, nie okno modalne. Modal utrudniłby to, co jest tu naturalne:
podejrzeć strukturę tabeli docelowej w innej zakładce, sprawdzić zapytaniem, ile jest już rekordów,
wrócić.

### 3.1. Układ całości

> ⭐ **Układ zrewidowany 2026-07-26 po pierwszym przeglądzie działającego I5** (§3.8). Rysunek niżej to stan
> obowiązujący; poprzedni — z pasem A i sekcjami Źródło \| Cel **obok siebie** — jest opisany na końcu tej
> sekcji razem z powodem odejścia od niego. Zmiana dotyczy wyłącznie rozmieszczenia i proporcji: modele,
> pipeline, `ImportConfiguration` i podział warstw są nietknięte.

```
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│ ⚠ <MessageBanner Classes="docked"> — tylko gdy jest komunikat                            │  C
├─────────────────────────────────────────────────────────────────────────────────────────┤
│ [⭳ Importuj (F5)] [✓ Waliduj] [■]  │ Transakcja [Ręczna ▾]  Błędy [Zatrzymaj ▾] │ 00:00 │  B
├─────────────────────────────────────────────────────────────────────────────────────────┤
│ Gotowość: ✓Źródło ✓Format ⚠Cel ⚠Mapowanie ✓Transakcja      (max 3 treści + „…i kolejne") │  D
├─────────────────────────────────────────────────────────────────────────────────────────┤
│ (•) Plik [ C:\…\lista.csv            ] [📂] ( ) Schowek   │ ▸ Opcje formatu               │  E1
│ 2,4 MB · 2026-07-24 14:02 · WIN1250 · ";" · DMY                                          │
├─────────────────────────────────────────────────────────────────────────────────────────┤
│ (•) Istniejąca [🔍 XXX_TMP_IMPORT ▾] ( ) Nowa                                            │  E2
│ 4 kolumny · 0 rekordów · klucz główny: brak · triggery BEFORE INSERT: brak                │
├─────────────────────────────────────────────────────────────────────────────────────────┤
│ MAPOWANIE (siatka)              ║ splitter ║              PODGLĄD PO KONWERSJI (siatka)  │  F
│ … (patrz §3.5)                                                    … (patrz §3.6)         │  ★
├══════════════════════════ GridSplitter (przeciągalny) ══════════════════════════════════┤
│ Podgląd źródła │ Błędy (2) │ Raport                        (bottom-tab, zwijany)         │  G
├─────────────────────────────────────────────────────────────────────────────────────────┤
│ SZKOLENIE · linia Data │ 1 240 wierszy · 3/4 kolumn zmapowanych · transakcja: ręczna     │  H
└─────────────────────────────────────────────────────────────────────────────────────────┘
```

| Pas | Zawartość | Wiersz | Ponowne użycie |
|---|---|---|---|
| **A** | ~~Nagłówek zakładki~~ — **USUNIĘTY** (U3). Żaden inny moduł EmberTerna go nie ma, a tytuł niesie zakładka. Jedyny fakt, który miał nieść *ponad* tytuł — połączenie i linia — przeniesiony do pasa **H** | — | — |
| **B** | Pasek poleceń: `Importuj` (`Classes="primary"`), `Waliduj`, `Anuluj` (widoczny tylko w trakcie), tryb transakcji, polityka błędów, `ExecutionTimer` **dokowany po prawej** (nie przesuwa przycisków — wzorzec ze Script Executora) | `Auto` | `Button.primary` / `Button.icon`, `SvgIcon`, `ExecutionTimer` |
| **C** | Jedyna powierzchnia komunikatów | `Auto` | `Controls/MessageBanner`, `Classes="docked"` |
| **D** | **Pasek gotowości** — patrz §3.2. Chipy zawsze; treści **z sufitem** (U6) | `Auto` | koncepcja `DebugPreflight` + mapowanie `Severity`→brush z `MessageBanner` |
| **E1** | **Kafelek ŹRÓDŁO** — picker **zawsze żywy**, zwija się wyłącznie „Opcje formatu" (§3.3) | `Auto` | idiom „chevron + tytuł, bez `Expander`" z panelu uruchomienia debuggera |
| **E2** | **Kafelek CEL** — picker tabeli zawsze żywy, pod nim linia faktów (§3.4) | `Auto` | `SearchableComboBox` |
| **F** | **Główna powierzchnia pracy: mapowanie ↔ podgląd**, rozdzielone `GridSplitter` | ⭐ **`*`** | wzorzec siatek + `GridLayoutBehavior` + `GridProfile` |
| **G** | Dolny `TabControl` + **własny `GridSplitter`**, zwijany podwójnym kliknięciem paska zakładek, **wysokość zapamiętywana trwale** (U2) | `Auto` \| px | `TabItem.bottom-tab`, mechanizm zwijania z debuggera/SQL Editora (**gotcha #240 — pełna renormalizacja obu wierszy przy każdym przełączeniu**) |
| **H** | **Gdzie wiersze lądują** (połączenie + linia), potem liczby. Nigdy przymiotniki | `Auto` | — |

**⭐ Reguła układu, z której wynika reszta: gwiazdka należy do PRACY, nie do konfiguracji.** Do przeglądu I5
wierszem `*` był `ScrollViewer` z konfiguracją, a podgląd miał przybite 190 px — odwrotnie, i to była
przyczyna „podglądu praktycznie nie widać". Konfiguracja ma naturalny rozmiar i się zwija; mapowanie i
podgląd to miejsce, w którym użytkownik spędza sesję, więc dostają wszystko, co zostanie.

**Odejście od pierwotnej makiety — powód, nie erratum.** Wersja v2 stawiała **Źródło i format \| Cel obok
siebie** i zwijała każdą sekcję do jednej linii podsumowania. Nie była błędna: obok siebie wysokość dwóch
rozwiniętych sekcji to **max**, nie suma, i to był świadomy zysk. Praktyka pokazała jednak dwie rzeczy,
których makieta nie mogła przewidzieć, bo widać je dopiero na działającym interfejsie:

1. **Stan „obie sekcje rozwinięte" jest wyjątkiem, nie regułą.** Praca jest sekwencyjna — wskazuję plik,
   sekcja się domyka, *dopiero potem* wybieram tabelę. Układ obok siebie optymalizował więc stan rzadki,
   płacąc **porządkiem czytania**, który jest stały; a §1.2 wymienia „kolejność źródło → format → cel →
   mapowanie → podgląd, czytaną **z góry na dół**" jako jedną z dwóch rzeczy, które świadomie zachowaliśmy
   z kreatora. Pion przywraca ją wprost.
2. **Sekcja Źródło mieszała dwie skrajnie różne częstotliwości zmian.** Ścieżka pliku zmienia się **przy
   każdym uruchomieniu**; separator, kodowanie i format daty — po pierwszym ustawieniu praktycznie nigdy.
   Zwinięcie ich razem znaczyło, że wskazanie kolejnego pliku kosztuje rozwinięcie sekcji i zwinięcie jej
   z powrotem — czyli łamie obietnicę z §1.2, że **powtarzalny import kosztuje jedno `F5`**.

Stąd podział kafelka nie na „sekcję i podsumowanie", lecz na **tożsamość (zawsze żywą)** i **szczegóły
(zwijane)**. To samo dotyczy Celu: zmiana tabeli nie wymaga rozwijania formularza.

**Zero nowych kolorów** poza ewentualnym jednym tokenem dla „kolumna niezmapowana" — najpierw sprawdzić,
czy `SubtleForegroundBrush` + kursywa nie wystarczą (wystarczyły w debuggerze dla `Restored`).
**Jedna nowa ikona** `Icon.Import` — kanoniczny `.svg` w `Assets/Icons/Actions/` + geometria w
`IconGeometries.axaml`, zgodnie z regułą systemu ikon D15.2. *(`Icon.Download` to strzałka pobierania —
semantycznie inna; do rozstrzygnięcia przy makiecie ikon.)*

⚠ **Czego ta rewizja NIE robi:** nie dotyka `Themes/ControlStyles.axaml` i nie zmienia wysokości ani jednej
kontrolki. Gęstość kontrolek to osobny **sprint UX całego EmberTerna po zamknięciu modułu** (§3.8/U4).
Granica obowiązująca w module: *wysokość `ComboBoxa`* to sprint, *to, że dwa `ComboBoxy` stoją jeden pod
drugim zamiast obok siebie* to ten moduł.

**Zero nowych kolorów** poza ewentualnym jednym tokenem dla „kolumna niezmapowana" — najpierw sprawdzić,
czy `SubtleForegroundBrush` + kursywa nie wystarczą (wystarczyły w debuggerze dla `Restored`).
**Jedna nowa ikona** `Icon.Import` — kanoniczny `.svg` w `Assets/Icons/Actions/` + geometria w
`IconGeometries.axaml`, zgodnie z regułą systemu ikon D15.2. *(`Icon.Download` to strzałka pobierania —
semantycznie inna; do rozstrzygnięcia przy makiecie ikon.)*

### 3.2. Pasek gotowości (zastępuje przyciski „Dalej")

```
Gotowość:  ✓ Źródło   ✓ Format   ✓ Cel   ⚠ Mapowanie 3/4   ✓ Transakcja
                                          └─ klik → rozwija i fokusuje sekcję Mapowanie
```

- Pozycje są **czystą funkcją Core**: `ImportReadiness.Evaluate(configuration, sourceSchema, target,
  transactionState)` → lista `ReadinessItem { Code, Severity, IsBlocking, TargetSection }`.
  Zero logiki w widoku — dokładnie tak, jak `DebugPreflightItem.BannerSeverity` przeniósł decyzję
  z widoku do modelu.
- **Blokujące** (czerwone, blokują `Importuj`): brak źródła, źródło nieczytelne, brak celu, zero
  zmapowanych kolumn, kolumna `NOT NULL` bez wartości domyślnej i bez mapowania, otwarta transakcja
  robocza, brak połączenia.
- **Ostrzegawcze** (żółte, nie blokują): część kolumn niezmapowana, przewidywane przekroczenia długości,
  aktywne triggery `BEFORE INSERT` na tabeli docelowej, tryb wsadowy (nieatomowy), włączone przycinanie
  wartości.
- **Każda pozycja jest klikalna** i rozwija + fokusuje sekcję, która ją powoduje. To jest przewaga nad
  kreatorem: użytkownik widzi **wszystkie** braki naraz.
- Gdy wszystko jest zielone, pas zwija się do jednej linii: `✓ Gotowe do importu — 1 240 wierszy`.

### 3.3. Sekcja **Źródło i format**

Rozwinięta (pierwsze użycie):

```
▾ ŹRÓDŁO I FORMAT
  ( • ) Plik   [ C:\…\Fantomy - Lista dla Streamsoft-1.xlsx        ] [📂]  ( ) Schowek
        Typ [ Excel (.xlsx) — wykryty z rozszerzenia ▾ ]     2,4 MB · 2026-07-24 14:02

  ── Parsowanie ─────────────────────  ── Kultura danych ──────────────────
  (wariant zależny od providera —      Separator dziesiętny  [ , ▾ ]
   patrz niżej)                        Separator tysięcy     [ (brak) ▾ ]
                                       Format daty           [ DMY ▾ ]  Sep. [ . ]
                                       Separator czasu       [ : ]
                                       Wartości logiczne     [ 1/0 · T/N ▾ ]
                                       Wartość NULL          [ pusty ciąg ▾ ]
```

Zwinięta (stan docelowy przy powtarzalnej pracy):

```
▸ ŹRÓDŁO I FORMAT   Fantomy - Lista.xlsx · Arkusz1 · w. 2–1241 · WIN1250 · ";" · DMY   [Zmień]
```

**Wariant parsowania a) tekst rozdzielany (CSV / TXT / Schowek)**

```
  Separator kolumn [ ; (średnik) ▾ ]  ☑ wykryj automatycznie
        └ wykryto „;" — 240/240 wierszy ma tę samą liczbę kolumn
  Separator tekstu [ " ▾ ]      Kodowanie [ Windows-1250 ▾ ]  ☑ wykryj automatycznie
        └ BOM: brak → heurystyka → Windows-1250
  Koniec linii [ auto (CRLF) ▾ ]
  ☑ Pierwszy wiersz zawiera nazwy kolumn
  Pierwszy wiersz danych [ 2 ]   Ostatni [ (do końca) ]
  ☐ Przytnij białe znaki na końcach wartości
```

**Wariant parsowania b) arkusz (XLSX / XLS)**

```
  Arkusz [ 0 — Arkusz1 (1 240 wierszy × 4 kolumny) ▾ ]
  ☑ Pierwszy wiersz zawiera nazwy kolumn
  Pierwszy wiersz danych [ 2 ]   Ostatni [ (do końca) ]
  ☑ Traktuj komórki dat jako daty  (inaczej: liczba seryjna Excela)
  ☐ Traktuj wartości puste jako NULL
```

Wybór wariantu **nie jest rozgałęzieniem w widoku po `enum`**, lecz projekcją
`IImportProvider.Capabilities` (odpowiednik `ExportCapabilities`) — nowy provider = nowe możliwości,
bez dotykania XAML-a.

**„Ostatni wiersz: (do końca)"** — nigdy `2147483647`. Interfejs nie pokazuje szczegółów implementacji.

### 3.4. Sekcja **Cel**

```
▾ CEL
  ( • ) Istniejąca tabela  [ 🔍 XXX_GG_TMP_IMPORT_FANTOM              ▾ ]   ← SearchableComboBox
        4 kolumny · 0 rekordów · klucz główny: brak · triggery BEFORE INSERT: brak
        ☐ Opróżnij tabelę przed importem  (DELETE FROM — w tej samej transakcji)   ← D5

  (   ) Nowa tabela   Nazwa [ XXX_GG_TMP_IMPORT_FANTOM_2 ]
        Typy wywnioskowane z 240 przeanalizowanych wierszy — edytowalne:
        ┌──────────────────┬───────────────┬──────┬───────────────────────┐
        │ Kolumna          │ Typ           │ NULL │ Podstawa              │
        ├──────────────────┼───────────────┼──────┼───────────────────────┤
        │ INDEKS_KARTOTEKI │ VARCHAR(20)   │  ☑   │ tekst, max 18 znaków  │
        │ NR_TECHNOLOGII   │ INTEGER       │  ☑   │ 240/240 liczb całk.   │
        │ NAZWA_FANTOMU    │ VARCHAR(100)  │  ☑   │ tekst, max 71 znaków  │
        └──────────────────┴───────────────┴──────┴───────────────────────┘
        [ Pokaż DDL ]  ⚠ Tabela zostanie utworzona i ZATWIERDZONA przed importem.
                          Wycofanie importu jej nie usunie.
                          ☐ Usuń tabelę, jeśli import się nie powiedzie
```

- Lista tabel z linii **Metadata** (read-only), przez istniejący `SearchableComboBox`.
- Typ w siatce nowej tabeli jest **edytowalny** (semantyka siatki pól z `NewTableTabView`; reguły
  rozmiar/skala/subtyp z `FieldTypeRules`). Kolumna **„Podstawa"** tłumaczy, *dlaczego* taki typ — to
  odpowiedź na „skąd on to wziął".
- „Pokaż DDL" renderuje wynik `DdlGenerator.BuildCreateTable(name, spec)` — **ten sam generator**,
  którego używa kreator nowej tabeli. Zero drugiej ścieżki.
- Opróżnianie tabeli pokazuje aktualną liczbę rekordów i wymaga potwierdzenia przy starcie.

### 3.5. Panel **Mapowanie**

```
Dopasowano po nazwie 3 z 4 kolumn.        [Dopasuj po pozycji] [Wyczyść] [Tylko niezmapowane ☐]

┌────────────────────────┬──────────────────────────┬───────────────┬──────────────┐
│ Kolumna docelowa       │ Pole źródłowe            │ Typ docelowy  │ Zgodność     │
├────────────────────────┼──────────────────────────┼───────────────┼──────────────┤
│ INDEKS_KARTOTEKI       │ [A  Indeks kartoteki ▾]  │ VARCHAR(20)   │ ✓            │
│ NR_TECHNOLOGII         │ [B  Nr technologii   ▾]  │ INTEGER       │ ✓            │
│ KOD_FANTOMU            │ [C  Kod fantomu      ▾]  │ VARCHAR(20)   │ ⚠ 26 > 20    │
│ NAZWA_FANTOMU          │ [— nie importuj —    ▾]  │ VARCHAR(100)  │ ⓘ pominięta  │
│ ID  (identity ALWAYS)  │ [— generowana —      ▾]  │ INTEGER       │ 🔒 systemowa │
│ WARTOSC_NETTO (comp.)  │ [— wyliczana —       ▾]  │ NUMERIC(15,2) │ 🔒 wyliczana │
└────────────────────────┴──────────────────────────┴───────────────┴──────────────┘
Pola źródłowe niewykorzystane:  D „Nazwa fantomu"
```

- **Orientacja cel → źródło.** Wierszem jest kolumna tabeli, bo to ona ma wymagania (NOT NULL, typ,
  długość). Pole źródłowe jest wyborem.
- **Kolumny systemowe są zablokowane, nie ukryte**, i mówią dlaczego. `COMPUTED BY` nie da się zapisać
  nigdy. Identity `GENERATED ALWAYS` można zmapować **tylko** po jawnym odblokowaniu — wtedy `INSERT`
  dostaje `OVERRIDING SYSTEM VALUE` (fakt już zamodelowany w `ColumnSpec.Identity`).
- **Diagnostyka jest przewidywaniem z próbki, nie wróżeniem** — zawsze z liczbą wierszy, których dotyczy.
- Pochodzenie mapowania renderowane jak `ValueOrigin` w debuggerze: `Auto/Restored` = cicha kursywa
  (`SubtleForegroundBrush`), `Assumed` = `AccentBrush` półgruby + słowo „założone" (**akcent, nie
  ostrzeżenie** — „warto zerknąć", nie „błąd"), `Manual` = zwykły tekst.
- Lista niewykorzystanych pól źródłowych czyni widocznym najczęstszy błąd „zapomniałem o kolumnie".

### 3.6. Panel **Podgląd po konwersji**

```
Podgląd 100 pierwszych wierszy PO KONWERSJI — dokładnie to trafi do bazy.        [🔄]
┌───┬──────────────────┬────────────────┬──────────────┐
│ # │ INDEKS_KARTOTEKI │ NR_TECHNOLOGII │ KOD_FANTOMU  │   ← standardowy DataGrid EmberTerna
├───┼──────────────────┼────────────────┼──────────────┤     GridId="DataImport.Preview"
│ 2 │ GN-375-GTO-2KAB… │         11 881 │ EOP-375-GTO… │     Record N of M, Copy, Export
│ 3 │ GN-375-GTO-2KAB… │         11 881 │ EOP-375-GTO… │
│ 4 │ GN-375-GTO-2KAB… │  ✖ „11 88x"    │ EOP-375-GTO… │   ← błąd: marker + wartość surowa
└───┴──────────────────┴────────────────┴──────────────┘
```

- **Podgląd jest po konwersji i ciągły.** Liczba wyrównana do prawej znaczy „serwer dostanie liczbę".
  Zmiana separatora dziesiętnego w sekcji Format przelicza tę siatkę (debounce 150 ms).
- Surowe rekordy providera są w dolnej zakładce **„Podgląd źródła"** — z markerem ostrzeżenia w gutterze
  dla wiersza o nietypowej liczbie kolumn (natychmiast widać źle dobrany separator, bez czytania).
- Siatka używa wspólnego wzorca (kolejność/szerokości kolumn w `GridProfile`, wskaźnik „Rekord N z M",
  kopiowanie). **Panel filtrów i pasek agregacji świadomie nie są podpięte** — filtrowanie danych przed
  importem jest osobną, zaplanowaną funkcją (§9.5), a filtr na podglądzie sugerowałby wpływ na import.
  To granica zakresu, nie przeoczenie.

### 3.7. Uruchomienie, postęp i raport

Uruchomienie nie zmienia układu — zmienia się tylko pasek poleceń i dolna zakładka **Raport**.

**W trakcie** (pasek B + H):

```
[■ Anuluj]  ████████████████░░░░░░░  62 %   773 / 1 240 · 0 błędów        00:04.1
```

Postęp raportowany co ~200 wierszy / 100 ms (dławiony `IProgress`, jak w Script Executorze).
Sekcje konfiguracyjne są w trakcie importu **tylko do odczytu** (nie wyszarzone do nieczytelności —
konfiguracja pozostaje widoczna, bo to ona tłumaczy, co się dzieje).

**Raport** (dolna zakładka, automatycznie aktywowana po zakończeniu):

```
✓ Zaimportowano 1 238 z 1 240 wierszy.  2 wiersze odrzucone.  Czas 00:06.4
  Transakcja OTWARTA — zatwierdź lub wycofaj.       [✓ Zatwierdź]  [↶ Wycofaj]

Błędy (2)                                    [Eksportuj raport…] [Kopiuj]
┌───────┬────────────────┬─────────────┬──────────────────────────────────┐
│ Wiersz│ Kolumna        │ Wartość     │ Powód                            │
├───────┼────────────────┼─────────────┼──────────────────────────────────┤
│  118  │ NR_TECHNOLOGII │ „11 88x"    │ Nie jest liczbą całkowitą        │
│  944  │ KOD_FANTOMU    │ „EOP-375-…" │ Za długa: 26 znaków, limit 20    │
└───────┴────────────────┴─────────────┴──────────────────────────────────┘
                             (podwójne kliknięcie → wiersz w podglądzie)
```

- Przyciski Commit/Rollback są **w raporcie**, nie tylko w globalnym pasku transakcji — decyzja zapada
  tam, gdzie są liczby.
- „Eksportuj raport" używa **istniejącego frameworka eksportu** (lista błędów jako `IExportDataSource`)
  → CSV/XLSX/schowek za darmo, bez ani jednej nowej linii serializacji.
- Po zakończeniu **konfiguracja pozostaje na miejscu** — kolejny plik tego samego kształtu to zmiana
  ścieżki i `F5`. Nie ma przycisku „Nowy import", bo nie ma z czego wychodzić.

### 3.8. ⏳ Uwagi z przeglądu wzrokowego I5 (2026-07-26) — OTWARTE, czekają na decyzję

> **Status: WSZYSTKIE PUNKTY ROZSTRZYGNIĘTE 2026-07-26; U1/U2/U3/U6/U7/U8/U9/U10/U11 DOSTARCZONE w szwie
> domykającym I5.** Punkty **U1–U5** pochodzą z przeglądu użytkownika, **U6–U10** z zamówionego przy tej
> samej okazji autoprzeglądu UX, **U11** wyszło przy projektowaniu układu. Żaden nie zmienił architektury
> modułu — wszystkie dotyczą proporcji, przestrzeni i tego, w którym pasie mieszka dana informacja;
> `ImportConfiguration`, pipeline i podział warstw są nietknięte. Rewizja układu jest wniesiona **w miejscu**
> do §3.1 (z powodem odejścia od makiety v2), a tabela niżej zostaje jako zapis, **co** zgłoszono i **jak**
> to rozstrzygnięto.
>
> **Otwarte zostają dwa punkty i oba są świadomie poza tym szwem: U4** (gęstość globalna → sprint UX całego
> EmberTerna po zamknięciu modułu) i **U5** (responsywność układu I6/I7 → weryfikowana przy I6, gdy sekcja
> Cel i panel Mapowanie faktycznie zajmą miejsce).

#### Uwagi użytkownika

| # | Uwaga | Stan faktyczny w kodzie | Zakres |
|---|---|---|---|
| **U1** | **Sekcja parametrów zajmuje za dużo pionu** — podgląd źródła praktycznie nie jest widoczny, przestrzeń robocza znika natychmiast | Rozwinięta sekcja *Źródło i format* to ~14 wierszy kontrolek w dwóch kolumnach (`Parsowanie` ~6 wierszy + 2 dodatkowe siatki, `Kultura danych` 5 wierszy). Kolumny są **niezbalansowane**: lewa jest wyraźnie wyższa, prawa kończy się pustką, a wysokość całości bierze się z wyższej | moduł |
| **U2** | **Dolny panel nie ma splittera** — ma zachowywać się jak dolne panele SQL Editora i Debuggera: przeciąganie wysokości, pełne zwijanie, **zapamiętywanie ostatniej wysokości** | Pas G ma dziś `Height="190"` na sztywno, wiersz `Grid.Row=4` jest `Auto`, `GridSplitter` **nie istnieje**, a zwijanie to sam `IsVisible`. Dodatkowo §3.1 obiecuje zwijanie **podwójnym kliknięciem paska zakładek** — też niezbudowane | moduł |
| **U3** | **Nagłówek „Data Import" (pas A) jest zbędny** — żaden inny moduł EmberTerna go nie ma, tytuł zakładki wystarcza, pion do odzyskania | Pas A pokazuje **wyłącznie** ikonę + tytuł. Uwaga trafia w sedno podwójnie: §3.1 przewidywał w tym pasie także **aktywne połączenie i linię** („SZKOLENIE.FDB · linia Data") — zbudowana została redundantna połowa, informacyjna nie | moduł |
| **U4** | **Gęstość interfejsu całej aplikacji** — globalny styl daje za wysokie `TextBox` / `ComboBox` / `CheckBox` / `Button`, za duże odstępy pionowe i wysokości wierszy formularzy. **Nie rozwiązywać lokalnie** | Potwierdzone w kodzie: `ControlStyles.axaml` **nie ma ani jednego stylu domyślnego** dla `TextBox`, `ComboBox`, `CheckBox`, `RadioButton`, `NumericUpDown` ani gołego `Button` — wszystkie siedzą na wartościach FluentTheme (`MinHeight` 32 px). Zagęszczone są tylko `DataGridRow`/`DataGridCell` i `TabItem`, i to **doraźnie, po jednym**. Czyli precedens istnieje, brakuje uogólnienia | ⛔ **BACKLOG — poza modułem.** Decyzja użytkownika 2026-07-26: osobny **sprint UX całego EmberTerna po zamknięciu Data Import**, projektowany z oglądu wszystkich modułów naraz. **Nie implementować niczego globalnego w etapach importu** |
| **U5** | **Responsywność** — na Full HD część formularza już nie mieści się w pionie; przeanalizować układ I5–I7 pod kątem wykorzystania przestrzeni i **zaproponować zmiany przed implementacją**, jeśli praktyka pokazuje lepsze rozwiązanie niż projekt | Realne: I6 dokłada sekcję **Cel** i panel **Mapowanie**, I7 pas **B** i panel **Podgląd po konwersji** — czyli do pionu, który już jest ciasny, dochodzą cztery obszary. R13 w §7 przewidywał to ryzyko dla 1366×768; przegląd pokazał, że dotyczy już 1920×1080 | moduł, przed I6 |

#### Propozycje z autoprzeglądu UX (nie zamawiane przez uwagi wyżej)

| # | Obserwacja | Propozycja |
|---|---|---|
| **U6** | **Pasek gotowości nie ma sufitu.** §3.2 gwarantuje zwinięcie do jednej linii, gdy wszystko jest zielone — ale przypadek odwrotny (wiele braków) rośnie bez ograniczenia: każdy wynik to własny zawijany wiersz, więc pas potrafi zająć ponad 100 px **na stałe nad** powierzchnią roboczą, i to dokładnie wtedy, gdy użytkownik najbardziej potrzebuje widzieć dane | **Chipy zostają zawsze** (to one realizują ratyfikowaną przewagę „wszystkie braki naraz" — kolorem, w jednej linii), a **lista treści dostaje limit** (np. 2–3 pozycje + „…i 2 dalsze") rozwijany kliknięciem. ⚠ To dotyka obietnicy z §3.2, więc **wymaga jawnej zgody**, nie jest kosmetyką |
| **U7** | **Kolumny `Parsowanie` \| `Kultura danych` są niezbalansowane i marnują szerokość.** Wszystkie pola mają stałe szerokości 70–160 px, a kolumna zajmuje 50% powierzchni — przy 1920 px to ~900 px na kontrolkę szeroką na 110 px. Pion rośnie, poziom stoi pusty | Zagęścić **poziomo, nie pionowo**: pary pól, które zawsze czyta się razem (`Separator dziesiętny` + `Separator tysięcy`, `Format daty` + `Sep. daty` + `Sep. czasu`), postawić w jednym wierszu. Kultura schodzi z 5 wierszy do 2–3, Parsowanie analogicznie. **Zero zmian w VM** — to sam XAML |
| **U8** | **Wiersz poszarpany nie jest oznaczony, choć jest wyliczony.** `ImportSourceRecordRowViewModel.IsRagged` istnieje, jest pinowane testem (i to jednym z ⭐ — „większość, nie najszerszy"), ale **nic go nie maluje**: kolumny podglądu buduje code-behind, marker w gutterze z §3.6 nie powstał | Domknąć sygnał albo świadomie go odłożyć **z datą**. Dziś to dokładnie kształt z gotchy #233 — rzecz przetestowana i niewywoływana wygląda potem jak regresja, a zielony zestaw testów to ukrywa. §3.6 stawia ten marker jako powód, dla którego źle dobrany separator „widać bez czytania" |
| **U9** | **Pas H (status powierzchni) i pas A niosą razem mniej, niż powinny.** Pas A pokazuje tytuł, którego użytkownik nie potrzebuje; pas H to jedna linia tekstu, a §3.1 chciał w pasie A **połączenia i linii transakcyjnej** — faktu, który w module piszącym do bazy jest istotny | Przy usuwaniu pasa A (U3) **przenieść połączenie + linię do pasa H**, obok liczb. Pas H i tak jest miejscem, gdzie w I7 stanie tryb transakcji — jedna linia u dołu odpowiada wtedy na „gdzie to wchodzi i w jakiej transakcji" |
| **U10** | **Skróty z §9.2 nie istnieją**, w tym te, które **mogłyby** działać już dziś: `Ctrl+O` (wybór pliku) i podwójne kliknięcie paska zakładek. Reszta (`F5`, `Ctrl+F5`, `Esc`, `F6`, `Ctrl+1..4`) słusznie czeka na polecenia, których jeszcze nie ma | Dołożyć **tylko te, które mają czym sterować** (`Ctrl+O`, podwójne kliknięcie), resztę zostawić do I7. Skrót do polecenia, którego nie ma, byłby martwym wpięciem — ta sama zasada, dla której I5 nie zbudował pasa B |

#### Kolejność prac — ⭐ ROZSTRZYGNIĘTA PRZEZ UŻYTKOWNIKA 2026-07-26

> **Decyzja: najpierw kończymy moduł Data Import, dopiero potem osobny sprint UX całego EmberTerna.**
> Zasada „jedno zadanie na raz" obowiązująca w projekcie od początku — przy okazji jednego modułu nie
> zaczynamy przebudowy całej aplikacji, bo zakres przestaje być czytelny i nie da się ocenić postępu.
> **To odwraca kolejność, którą wcześniej rekomendowałem („najpierw gęstość globalna"); rekomendacja jest
> nieaktualna i nie należy do niej wracać.**

1. **U4 nie jest zadaniem tego modułu i NIE jest teraz realizowane.** Ląduje w **backlogu projektu** jako
   przyszły **sprint UX całego EmberTerna**, świadomie zaplanowany **po zamknięciu modułu**: sens tego
   sprintu polega na obejrzeniu wszystkich powierzchni **naraz** (SQL Editor, Debugger, Activity Monitor,
   Session Manager, Script Executor, Data Import i pozostałe) i zaprojektowaniu globalnego stylu kontrolek
   na tej podstawie — a nie na wywnioskowaniu go z jednego formularza. **Żadnych zmian w
   `Themes/ControlStyles.axaml` w ramach etapów importu.**
2. **W module poprawiamy wyłącznie to, co NIE wynika z globalnego stylu** — układ, proporcje, ergonomię
   i braki funkcjonalne powierzchni: **U1 (część układowa), U2, U3, U5, U7, U9, U10** (U6 i U8 — patrz
   niżej, czekają na osobną decyzję). Rozróżnienie jest ostre i warto je trzymać: *wysokość pojedynczego
   `ComboBoxa`* to sprint UX, *to, że dwa `ComboBoxy` stoją jeden pod drugim zamiast obok siebie* to ten
   moduł.
3. **U2, U3, U9, U10 tworzą jeden „szew domykający I5"** — są od siebie niezależne i żaden nie czeka na
   nic z zewnątrz.
4. **U5 rozstrzyga się przed I6**, bo dotyczy tego, gdzie w ogóle staną panele Cel i Mapowanie.
5. ⚠ **Konsekwencja przyjęta świadomie:** dostrajając układ modułu przed sprintem UX, dostrajamy go
   względem obecnych, za wysokich kontrolek — po sprincie sekcja Źródło i format odzyska jeszcze ~100 px
   i proporcje warto będzie obejrzeć ponownie. **Audyt UI w I12 i tak obejmuje obie palety oraz
   1366×768**, więc jest naturalnym miejscem tej powtórki.

#### Co dostarczył szew domykający I5 (2026-07-26)

| # | Rozstrzygnięcie | Jak zrealizowane |
|---|---|---|
| **U1 / U7** | zagęszczać **poziomo**, nigdy przez wysokość kontrolek | pary pól czytane razem dzielą wiersz (Kultura: 5 wierszy → 3; Parsowanie: cudzysłów+koniec linii, nagłówek+zakres wierszy, przycinanie+NULL); **zero zmian w `ControlStyles.axaml`** |
| **U2** | splitter + pełne zwijanie + **trwała** wysokość | `GridSplitter` + `ApplyBottomPanel` jako JEDYNY punkt renormalizacji **obu** wierszy (#240); podwójne kliknięcie paska; wysokość i stan zwinięcia w `WorkspaceState.ImportPreviewPanelHeight/Collapsed` — globalnie, jak `ResultsPanelHeight`, **nigdy w `ImportConfiguration`** (§4.8.2) |
| **U3 / U9** | pas A znika, jego fakt idzie do pasa H | pas H: `DestinationStatus` (połączenie + **linia Data**, czytane delegatem) │ liczby |
| **U6** | pasek gotowości dostaje sufit | `VisibleItems` = `Items` przycięte do `CollapsedItemLimit = 3` + „…i kolejne N" z rozwijaniem; **chipy zawsze wszystkie**; kolejność Core'a nietknięta (żadnego drugiego rankingu) |
| **U8** | marker wiersza poszarpanego domknięty **teraz** | kolumna numeru wiersza w podglądzie źródła niesie `⚠` + tooltip dla `IsRagged`; sygnał przestał być martwy (#233) |
| **U10** | tylko skróty, które mają czym sterować | `Ctrl+O` (wybór pliku) + podwójne kliknięcie paska zakładek; `F5`/`Ctrl+F5`/`Esc` czekają na polecenia z I7 |
| **U11** | ⭐ auto-zwijanie „Opcji formatu" po ustaleniu się źródła | zwija się **dopiero po udanym odczycie** (są pola) i **nigdy**, gdy użytkownik rozwinął je ręcznie (`_formatOptionsHeldOpen`) |
| **U12** | „Parsowanie" i „Kultura danych" mają wyglądać jak **grupy**, nie jak etykiety zawieszone między kontrolkami | nowy **współdzielony** idiom w `ControlStyles.axaml`: `Border.settings-group` (wpuszczona karta `BackgroundBrush` + ramka + `CornerRadius`) + `TextBlock.group-header` (mocniejszy od `field-label`, bo nagłówek nazywa **temat**, a `field-label` jedną **wartość**). Zero nowych kolorów, zero zmian metryk kontrolek. Przy okazji usunięty **martwy** `Border.panel` (0 konsumentów w całej aplikacji) — bliźniaczy styl obok nowego to gotowy sposób, żeby ktoś sięgnął po zły |

**Skutek liczbowy** (szacunek z układu, nie pomiar na uruchomionej aplikacji; 1920×1080, ~880 px na
zakładkę): w stanie ustalonym pas E to ~80 px zamiast ~470, a wiersz `*` — czyli przyszłe Mapowanie i
Podgląd — dostaje **~500–540 px**, gdzie dotąd nie miał ani jednego (podgląd źródła był przybity do 190 px).

⚠ **Jedna rzecz w U2 wymaga rozstrzygnięcia projektowego, nie tylko zgody.** „Zapamiętywanie ostatniej
wysokości" ma w aplikacji **dwa** precedensy, nie jeden: Debugger pamięta wysokość **w obrębie sesji**
(pole `_bottomHeight` w widoku), a SQL Editor **trwale** (`WorkspaceState.ResultsPanelHeight`). Zakładka
importu jest **nietrwała** (świadomie pominięta w `SnapshotCurrentTabs`), więc trwała wysokość nie ma gdzie
zamieszkać per zakładka — musiałaby być ustawieniem **globalnym** obok `ResultsPanelHeight`.
**I nie wolno jej włożyć do `ImportConfiguration`**: to preferencja układu, a nie decyzja użytkownika o
imporcie — §4.8.2 wyznacza tę granicę, a strażnik refleksyjny z §4.8.6 i tak zażądałby wtedy, żeby
wysokość panelu jeździła w profilu importu.

---

## 4. Architektura

### 4.1. Odwzorowanie warstw na rzeczywiste projekty EmberTerna

Prośba wymieniała warstwy *Core / Application / Infrastructure / UI / ViewModel*. **EmberTern nie ma
projektów `Application` ani `Infrastructure`** i ich dodanie byłoby regresją architektoniczną (reguła #5;
solution jest celowo płaskie). Odwzorowanie:

| Warstwa | Realne miejsce | Zasada |
|---|---|---|
| **Core (domena)** | `EmberTern.Core/Import/` | Modele, konfiguracja, pipeline, konwersja, wnioskowanie typów, gotowość, providerzy tekstowi. Zero Avalonia, zero `FirebirdSql`. |
| **Application (orkiestracja)** | `EmberTern.Core/Import/ImportPipeline.cs` + VM-y sekcji | Pipeline **jest** warstwą aplikacyjną — czysty, sterowany przez porty. VM tylko konfiguruje i pokazuje wynik. |
| **Infrastructure** | `EmberTern.Firebird/` (zapis + katalog), **`EmberTern.Office/`** (XLSX/XLS) | Jedyne miejsca ze sterownikiem / NuGetem. |
| **UI** | `EmberTern.App/Views/DataImportTabView.axaml` + 5 `UserControl` sekcji/paneli | Wyłącznie prezentacja + tokeny motywu. |
| **ViewModel** | `EmberTern.App/ViewModels/DataImport*ViewModel.cs` | Zero typów Avalonia (reguła #1). |

### 4.2. Nowe pliki — pełna lista

```
src/EmberTern.Core/Import/
    ImportModels.cs              # SourceField, SourceSchema, RawRecord, ImportTarget, ImportRow,
                                 #   ImportOutcome, ImportRowError, ImportProgress…
    ImportConfiguration.cs       # ⭐ JEDNA reprezentacja wszystkich decyzji użytkownika (§4.8)
    ImportProfile.cs             # ⭐ ImportConfiguration + metadane (Id, Name, LastUsedUtc…)
    ImportProfileStore.cs        # ⭐ fasada sekcji settings.dat (wzorzec WatchStore)
    ImportOptions.cs             # DelimitedOptions, SpreadsheetOptions, ImportCultureOptions
    ImportEnums.cs               # ImportSourceKind, ImportMode, ImportTransactionMode,
                                 #   ImportErrorPolicy, ImportErrorKind, MappingOrigin
    ImportContracts.cs           # IImportSource, IImportProvider, IImportWriter   ← 3 porty
    ImportPipeline.cs            # JEDEN pipeline: read → map → convert → validate → write
    ImportValueConverter.cs      # tekst/natywna → CLR wg ColumnSpec + kultury (ścisły)
    ImportMappingPlanner.cs      # auto-mapowanie + reguła zachowania dowodliwego + diagnostyka
    ImportReadiness.cs           # ⭐ czysta funkcja gotowości (pasek §3.2)
    ColumnTypeInferencer.cs      # próbki → FieldDefinition (zachowawczo, §0.3)
    ImportDiagnostics.cs         # kody IMP0001… (strukturalne, BEZ tekstów UI)
    DryRunImportWriter.cs        # druga implementacja IImportWriter (walidacja bez zapisu)
    Providers/
        DelimitedTextImportProvider.cs   # CSV / TXT / schowek
        DelimitedTextReader.cs           # RFC4180, cudzysłowy, pola wielolinijkowe
        DelimiterDetector.cs             # propozycja + podstawa decyzji
        EncodingDetector.cs              # BOM → heurystyka → propozycja
        FileImportSource.cs              # IImportSource nad plikiem
        TextImportSource.cs              # IImportSource nad string (schowek)

src/EmberTern.Office/                    # ⭐ D1: zmiana nazwy z EmberTern.Export.Office
    XlsxExporter.cs                      #   (przeniesiony bez zmian)
    XlsxImportProvider.cs
    XlsImportProvider.cs                 #   etap I10

src/EmberTern.Firebird/
    FirebirdImportWriter.cs      # przygotowany INSERT + re-bind na wiersz + paczki + mapa błędów
    FirebirdImportTargetReader.cs# lista tabel + ColumnSpec celu + triggery (linia Metadata)

src/EmberTern.App/ViewModels/
    DataImportTabViewModel.cs         # koordynator: JEDNA konfiguracja, przeliczanie, uruchomienie
    ImportSourceSectionViewModel.cs   # źródło + format (jedna zwijalna sekcja)
    ImportTargetSectionViewModel.cs   # cel istniejący / nowy + wnioskowane typy
    ImportMappingViewModel.cs
    ImportPreviewViewModel.cs
    ImportRunViewModel.cs             # postęp + raport + Commit/Rollback
    ImportReadinessViewModel.cs       # projekcja ImportReadiness na pasek §3.2
    ImportColumnMappingRowViewModel.cs
    ImportErrorRowViewModel.cs
    ImportErrorExportSource.cs        # IExportDataSource nad listą błędów

src/EmberTern.App/Views/
    DataImportTabView.axaml(.cs)      # rama: pasy A–H
    ImportSourceSectionView.axaml     ImportTargetSectionView.axaml
    ImportMappingView.axaml           ImportPreviewView.axaml
    ImportRunReportView.axaml

tests/EmberTern.Tests/
    ImportDelimitedReaderTests, ImportValueConverterTests, ImportMappingPlannerTests,
    ColumnTypeInferencerTests, ImportReadinessTests, ImportPipelineTests (fake writer),
    ImportConfigurationRoundTripTests  ⭐ (§4.8.6 — test, który chroni przed przebudową),
    ImportProfileStoreTests, DataImportTabVmTests

tools/probes/DataImportProbe/          # weryfikacja na żywym FB5 (etap I4)
```

**Usunięte względem v1:** wszystkie widoki kroków kreatora, `ImportFormatStepViewModel`,
`ImportRunStepViewModel` jako krok, oraz **`BreadcrumbBar` z listy ponownie użytych kontrolek** —
kreatora nie ma, szyny kroków nie ma.

### 4.3. Porty (interfejsy) — i dlaczego są legalne wobec reguły #2

Reguła #2 zakazuje interfejsów z jedną implementacją. Każdy port ma **co najmniej dwie konkretne
implementacje w kodzie produkcyjnym** (nie w testach):

| Port | Odpowiedzialność | Implementacje |
|---|---|---|
| `IImportSource` | Pochodzenie bajtów/tekstu (lustro `IExportSink`) | `FileImportSource`, `TextImportSource` |
| `IImportProvider` | Źródło → `SourceSchema` + `IAsyncEnumerable<RawRecord>` | `DelimitedTextImportProvider`, `XlsxImportProvider` (+ `XlsImportProvider`) |
| `IImportWriter` | Ujście wierszy | `FirebirdImportWriter`, `DryRunImportWriter` |

`DryRunImportWriter` nie jest atrapą testową — to funkcja produktu („Waliduj"), która przy okazji czyni
pipeline testowalnym bez bazy. **Żadnych innych interfejsów** — `ImportPipeline`, `ImportValueConverter`,
`ImportMappingPlanner`, `ImportReadiness`, `ColumnTypeInferencer`, `ImportProfileStore` są zwykłymi
klasami.

Szkic kontraktów (sygnatury poglądowe, nie kod do wklejenia):

```
IImportSource   : OpenTextAsync(Encoding) → TextReader ; OpenStreamAsync() → Stream
                  DisplayName ; SizeBytes? ; StillExists()
IImportProvider : ReadSchemaAsync(source, options, ct) → SourceSchema
                  ReadRecordsAsync(source, options, ct) → IAsyncEnumerable<RawRecord>
                  Capabilities → ImportProviderCapabilities    // arkusze? kodowanie? separatory?
IImportWriter   : BeginAsync(target, mapping, ct)
                  WriteAsync(ImportRow, ct)                     // przyjmuje wiersz do BIEŻĄCEJ paczki
                  FlushBatchAsync(ct) → IReadOnlyList<ImportBatchItemResult>
                  CompleteAsync(ct) → ImportWriteSummary
```

**⚠ (I0 / REK-1) Dlaczego `WriteAsync` NIE zwraca wyniku wiersza.** Zapis idzie paczkami
(`FbBatchCommand` — 16× szybszy od pętli, §4.5), a **w chwili dodania wiersza do paczki jego błąd jeszcze
nie istnieje**: powstaje przy wysłaniu paczki. Wynik per wiersz w sygnaturze `WriteAsync` byłby więc
kłamstwem. Dlatego:

- `WriteAsync` odpowiada **wyłącznie** za przyjęcie wiersza do bieżącej paczki,
- `FlushBatchAsync` **wykonuje zapis** i zwraca wynik **każdego** elementu paczki
  (`ImportBatchItemResult { IsSuccess, RecordsAffected, ErrorKind?, ServerMessage? }`) **w kolejności
  dodania** — pomiar potwierdził, że sterownik utrzymuje wyrównanie 1:1 między indeksem w paczce a
  indeksem w wyniku (I0 §2.3),
- **`ImportPipeline` utrzymuje okno „indeks w paczce → `SourceRowNumber`"** (rozmiaru paczki, domyślnie
  500 pozycji) i tłumaczy indeksy sterownika na numery wierszy źródłowych, **zanim** cokolwiek dotrze do
  raportu. To jedyne miejsce, w którym ta translacja żyje — raport nigdy nie widzi indeksu paczki.

`DryRunImportWriter` realizuje **ten sam** kontrakt: jego `FlushBatchAsync` zwraca wyniki walidacji,
których nie wysłał — dlatego „Waliduj" i prawdziwy import przechodzą identyczną ścieżką.

`ImportProviderCapabilities` jest odpowiednikiem `ExportCapabilities`: sekcja Format pyta providera,
**co ma pokazać**, zamiast rozgałęziać się po `enum`.

### 4.4. Pipeline — jedno przejście, wspólne dla wszystkich źródeł

```
ImportPipeline.RunAsync(configuration, provider, source, writer, progress, ct)
│
├─ 1. provider.ReadRecordsAsync(source, options)      →  RawRecord { SourceRowNumber, object?[] }
│        (strumień; NIGDY nie materializuje całego pliku)
├─ 2. ImportMappingPlanner.Project(record, mapping)   →  wartości surowe w kolejności kolumn celu
├─ 3. ImportValueConverter.Convert(raw, ColumnSpec, culture)
│        → Ok(value) | Error(ImportErrorKind, rawValue)         ← §0.1 / §0.2 mieszkają TUTAJ
├─ 4. ImportRowValidator                              →  NOT NULL bez default · długość · skala ·
│        ⚠ (I0/REK-2) reprezentowalność w charsecie POŁĄCZENIA — obowiązkowo, przez
│        Encoding.GetEncoding(<charset połączenia>) z EncoderExceptionFallback (NIGDY domyślnym
│        zastępczym): pomiar wykazał, że nieprzedstawialny znak jest CICHO zamieniany na '?',
│        także gdy kolumna docelowa jest UTF8 — patrz R1.
├─ 5. writer.WriteAsync(row) → paczka;  writer.FlushBatchAsync() → wynik KAŻDEGO elementu paczki
│        pipeline trzyma okno „indeks w paczce → SourceRowNumber" i tłumaczy indeksy na numery
│        wierszy źródłowych (§4.3); Firebird: FbBatchCommand, domyślnie 500 wierszy na paczkę,
│        commit co N w trybie wsadowym
├─ 6. polityka błędów: StopOnFirstError → przerwij  |  SkipInvalidRows → licz i jedź dalej
│        (I0 §2.3: odwzorowuje się 1:1 na FbBatchCommand.MultiError = false / true)
└─ 7. ImportOutcome { Read, Written, Failed, Errors(cap 1000), TransactionLeftOpen, Cancelled }
```

Cechy, które są celem, nie skutkiem ubocznym:
- **Pipeline nie wie, czy źródłem jest CSV czy Excel.** Wie tylko o `RawRecord`.
- **Pipeline nie wie, czy pisze do Firebirda.** Dry-run to zamiana jednego argumentu.
- **Pipeline nie zna tekstów UI.** Zwraca kody (`ImportErrorKind`, `IMP0001`), App mapuje na `UiStrings` —
  konwencja `ExportUnavailableReason` i `Diagnostic`.
- **Pipeline dostaje `ImportConfiguration`, nie 15 argumentów.** To ta sama wartość, którą zapisuje
  profil (§4.8) — nie ma drugiej reprezentacji „co uruchomić".

### 4.5. Model transakcyjny — najważniejsza decyzja modułu

| Operacja | Linia | Transakcja | Uzasadnienie |
|---|---|---|---|
| Lista tabel, kolumny celu, triggery | **Metadata** | niejawna, per-komenda | Linia Metadata jest read-only i nie posiada transakcji. |
| `CREATE TABLE` (nowa tabela) | **Ddl** | autonomiczna, auto-commit, WAIT (Developer Mode) | **Gotcha #213: transakcja nie może użyć obiektu, którego DDL nie zatwierdziła.** Utworzenie tabeli i wstawienie do niej danych w jednej transakcji jest w Firebirdzie niemożliwe. Linia Ddl już to rozwiązuje (Compile edytorów obiektów) — zero nowego mechanizmu. |
| `DELETE FROM` (opróżnij przed importem, D5) | **Data** | transakcja robocza użytkownika | To dane, nie schemat — musi być wycofywalne razem z importem. |
| `INSERT` wierszy | **Data** | **JEDNA transakcja robocza użytkownika** (auto-begin, nigdy auto-commit) | Reguła #3 + gotcha #89. Import to operacja użytkownika, tak jak F5 i Script Executor. |

**Konsekwencja, którą trzeba powiedzieć wprost (§0.5):** przy celu „nowa tabela" `Rollback` **nie usunie
tabeli**. Dlatego sekcja Cel pokazuje ostrzeżenie i oferuje jawny checkbox „usuń tabelę, jeśli import się
nie powiedzie" (wtedy `DROP TABLE` na linii Ddl, po potwierdzeniu). To samo uczciwe podejście, które
zastosowano w trybie `Sequenced` Script Executora: **nieatomowość jest ujawniona tam, gdzie zapada
decyzja**, a nie ukryta.

`ImportTransactionMode` celowo powiela słownik `ScriptTransactionMode` (`Manual` /
`AutoCommitOnSuccess`) i dokłada `Batched(N)` — jedyny tryb zdolny obsłużyć plik milionowierszowy bez
transakcji żyjącej godzinę (o której ostrzega własny detektor EmberTerna w Session Managerze).
**Domyślny jest `Manual` (D3);** `Batched` nigdy nie jest domyślny i zawsze niesie opis skutku.

**⚙ (I0 / REK-5) Wartości domyślne — zmierzone, nie wybrane intuicyjnie:**

| Ustawienie | Wartość | Podstawa pomiarowa |
|---|---|---|
| rozmiar paczki `FbBatchCommand` | **500 wierszy** | optimum 250–1 000 (~121 000 rows/s); przy 20 000 spadek 3,7× |
| `Batched` — commit co N | **10 000 wierszy** | commit jest praktycznie darmowy (co 100 wierszy = −4,5%), więc N wybieramy dla czytelności raportu, nie dla wydajności |
| próg ostrzeżenia „długa transakcja" (R4) | **100 000 wierszy** | dotyczy czasu ŻYCIA transakcji, nie czasu importu (ten to ~1 s) |
| `CommandLock` | brany **per paczka** | koszt poniżej progu mierzalności (mieści się w szumie ~4% między przebiegami) |

**Konsekwencja dla `Batched`:** skoro commit nic nie kosztuje, jedyną ceną tego trybu pozostaje
**nieatomowość** — czyli dokładnie to, co §0.5 i tak nakazuje ujawnić. Tryb zostaje.

### 4.6. Ponowne użycie — inwentarz

| Potrzeba | Istniejący komponent | Czy powstaje coś nowego? |
|---|---|---|
| Model kolumny docelowej (typ, NOT NULL, PK, identity, computed) | `Core.Metadata.ColumnSpec` | **nie** |
| Definicja pola nowej tabeli + DDL | `FieldDefinition` / `TableSpec` / `DdlGenerator.BuildCreateTable` | **nie** |
| Reguły rozmiar/skala/subtyp | `FieldTypeRules` | **nie** |
| Wykonanie DDL (WAIT, Developer Mode) | `FirebirdDdlExecutor` | **nie** |
| Transakcja robocza, Commit/Rollback, `CommandLock` | `TransactionService` | **nie** |
| Odczyt katalogu (tabele, kolumny, triggery) | `FirebirdMetadataReader` / `FirebirdTableDetailReader` | cienki adapter `FirebirdImportTargetReader` |
| Siatka podglądu / mapowania / błędów | wzorzec dynamicznych kolumn + `GridLayoutBehavior` + `GridProfile` | **nie** |
| Eksport raportu błędów | `EmberTern.Core.Export` (`IExportDataSource`) | adapter ~30 linii |
| Komunikaty błędów/ostrzeżeń | `Controls/MessageBanner` (`Classes="docked"`) | **nie** |
| Zwijalne sekcje konfiguracji | wzorzec „Advanced disclosure" z panelu uruchomienia debuggera | **nie** |
| Dolny panel z zakładkami + zwijanie | `TabItem.bottom-tab` + mechanizm z debuggera (gotcha #240!) | **nie** |
| Pasek gotowości | koncepcja + podział Severity z `DebugPreflight` | `ImportReadiness` (Core, czysty) |
| Wybór tabeli | `Controls/SearchableComboBox` | **nie** |
| Zegar operacji | `ExecutionTimer` | **nie** |
| Trwałość konfiguracji / profile | `settings.dat` + wzorzec fasady sekcji (`WatchStore`, `ParameterHistoryStore`) | `ImportProfileStore` |
| Zakładka narzędziowa (near-singleton, nietrwała) | wzorzec `WorkspaceTabKind.ScriptExecutor` | +1 wartość enuma |
| „Jakiego rodzaju jest ta wartość" | `Core.Export.Sql.SqlValueKind` + `FirebirdValueKindMap` | **nie** (używamy w drugą stronę) |
| Ścisłe parsowanie pod InvariantCulture | wzorzec `SqlLiteralWriter` (odmawia przy niejednoznaczności) | `ImportValueConverter` — **odwrotny kierunek tej samej dyscypliny** |

### 4.7. Przeliczanie i reguła zachowania dowodliwego

Bez kroków nie ma „unieważniania kroków w przód" — jest **jeden łańcuch przeliczeń**, wywoływany po
każdej zmianie konfiguracji:

```
zmiana źródła lub opcji formatu  →  ReadSchema  →  (mapowanie: zachowaj dowodliwe)  →  podgląd  →  gotowość
zmiana celu                      →  ReadTarget  →  (mapowanie: patrz niżej)         →  podgląd  →  gotowość
zmiana mapowania                 →                                                     podgląd  →  gotowość
zmiana kultury / opcji wartości  →                                                     podgląd  →  gotowość
```

Przeliczenie jest **leniwe i anulowalne**: nowa zmiana anuluje trwające czytanie schematu (wzorzec CTS
z `EditorLanguageService`). Odczyt schematu i podgląd idą na wątek tła; gotowość jest tania i liczona
synchronicznie.

**Reguła zachowania dowodliwego** — przeniesiona bez zmian z konfiguracji uruchomienia debuggera (C3),
bo problem jest identyczny: *moduł zachowuje wszystko, co potrafi UDOWODNIĆ, że nadal jest poprawne,
oddaje użytkownikowi wszystko, czego nie potrafi, i nigdy nie zgaduje.*

- **Dowód = zgodność nazwy.** Po zmianie formatu pole `Kod fantomu` nadal istnieje → mapowanie na
  `KOD_FANTOMU` zostaje, oznaczone `MappingOrigin.Restored`.
- **Reguła jedynej pary** — jeśli po zmianie zostaje dokładnie **jedna** niedopasowana kolumna po każdej
  stronie, para jest łączona i oznaczona `MappingOrigin.Assumed` (wizualnie odmiennie). Dwie lub więcej
  → **nic nie jest łączone**.
- **Zmiana tabeli docelowej** unieważnia mapowanie w całości (inna tożsamość celu), z komunikatem
  „mapowanie wyczyszczone, bo zmieniono tabelę docelową" — nigdy po cichu.
- Każda ręczna edycja kasuje znacznik pochodzenia — marker nigdy nie opisuje wartości, którą użytkownik
  już nadpisał.

*(W v1 dokumentu ta reguła obsługiwała też powroty między krokami. Ta część zniknęła razem z krokami —
to jest oszczędność złożoności, o której mówi §1.2 punkt 3.)*

---

### 4.8. ⭐ Architektura gotowa na profile — od pierwszego etapu

Wymaganie: **nie przebudowywać później UI ani modeli.** Pułapka, której trzeba uniknąć, jest konkretna:
jeśli konfiguracja żyje jako kilkadziesiąt rozproszonych `[ObservableProperty]` w VM-ach sekcji, to
dodanie profili później oznacza napisanie mapera w dwie strony po ~40 polach — i każde pole dodane w
międzyczasie zostanie w nim **pominięte**. To jest dokładnie ta przebudowa, której nie chcemy.

#### 4.8.1. Jedna zasada, z której wynika wszystko

> **Stan konfiguracji powierzchni JEST profilem.** `ImportConfiguration` to jedyna reprezentacja
> wszystkich decyzji użytkownika. Zapis profilu = serializacja tego rekordu. Wczytanie = przypisanie go.
> Uruchomienie importu = przekazanie go do pipeline'u. **Nie istnieje druga reprezentacja.**

Dzięki temu profile nie są funkcją do „dobudowania" — są konsekwencją modelu. Brakuje wyłącznie UI listy
nazwanych profili, a to jest widok nad istniejącym magazynem.

#### 4.8.2. `ImportConfiguration` — zakres

Rekord (niemutowalny, `record` z `init`), **serializowalny, bez typów UI i bez typów sterownika**:

```
ImportConfiguration
├─ Version : int                    // wersja schematu KONFIGURACJI (nie settings.dat!)
├─ Source : SourceDescriptor        // Kind (File|Clipboard) + Path? — NIGDY zawartość
├─ Delimited : DelimitedOptions?    // dokładnie jeden z tych dwóch jest ustawiony,
├─ Spreadsheet : SpreadsheetOptions?//   zależnie od Source.Kind / typu pliku
├─ Culture : ImportCultureOptions   // separatory, format daty, tokeny logiczne, token NULL
├─ Target : TargetDescriptor        // Kind (Existing|New) + TableName
│           └─ NewTableColumns : IReadOnlyList<ImportColumnDefinition>?   // tylko dla New
├─ Mapping : IReadOnlyList<ColumnMapping>
├─ Mode : ImportMode                // v1: Insert
├─ Transaction : ImportTransactionMode  + BatchSize : int
├─ ErrorPolicy : ImportErrorPolicy
└─ Behavior : ImportBehaviorOptions // EmptyTargetBeforeImport, TrimTooLongValues,
                                    //   TreatEmptyAsNull, DropTableOnFailure,
                                    //   ExcelErrorCellsAsNull   ← (I0/REK-6, addytywne; domyślnie false
                                    //     ⇒ komórka #N/A jest BŁĘDEM wiersza, nigdy tekstem)
```

**As-built (I1):** `TrimWhitespace` **nie leży** w `ImportBehaviorOptions`, mimo że powyższa lista v2 wymieniała
je w obu miejscach. Przycinanie białych znaków jest własnością *czytania pola tekstowego*, więc żyje w
`DelimitedOptions.TrimWhitespace` — tam, gdzie pokazuje je sekcja Format (§3.3a). Jedno pytanie, jeden
właściciel; dwa miejsca na tę samą decyzję byłyby dokładnie tym rodzajem rozdwojenia, przed którym broni §9.1.

Czego w profilu **nie ma i nie będzie**:
- **danych** (ani jednego wiersza) i zawartości schowka;
- **rozwiązanego `SourceSchema`** ani `ColumnSpec[]` celu — to fakty odczytane ze świata, nie decyzje
  użytkownika; są czytane na nowo przy każdym wczytaniu (to jest właśnie mechanizm z §4.8.5);
- poświadczeń, ścieżki do bazy, identyfikatora połączenia (te są **metadanymi profilu**, nie
  konfiguracją — patrz §4.8.3);
- liczników, czasów, wyników.

#### 4.8.3. `ImportProfile` i magazyn

```
ImportProfile
├─ Id : string (GUID)
├─ Name : string                  // "" ⇒ profil niejawny „ostatnio użyty"
├─ ConnectionId : string?         // null ⇒ przenośny między połączeniami
├─ CreatedUtc / LastUsedUtc : DateTime
└─ Configuration : ImportConfiguration
```

- **`ImportProfileStore`** — fasada sekcji nad współdzielonym, szyfrowanym `settings.dat`, dokładnie
  wzorcem `WatchStore` / `ParameterHistoryStore`: nowa lista `UserSettings.ImportProfiles`.
- **Wersjonowania kontenera `settings.dat` NIE ruszamy.** Nauka z C3: podbicie wersji kontenera
  uruchamia ochronę przed downgrade'em i starsza wersja aplikacji odmówiłaby odczytu **całego** pliku.
  Dodanie listy jest addytywne — stary plik ma po prostu listę pustą.
- Wersjonowanie żyje **wewnątrz** `ImportConfiguration.Version`: pola tylko dodajemy, brak pola ⇒
  wartość domyślna, nieznana wersja wyższa ⇒ profil oznaczony jako nieczytelny **z komunikatem**,
  nigdy wczytany po części.
- **Przenośność:** ponieważ profil to jeden rekord, eksport/import do pliku `.json` („podziel się
  konfiguracją importu z kolegą") jest trywialny. Nie w MVP, ale nic go nie blokuje.

#### 4.8.4. Co powstaje w MVP, a co później — i dlaczego mechanizm nie leży martwy

Gotcha #233 ostrzega: **komponent przetestowany, ale nigdzie nie wywołany, wygląda dokładnie jak
regresja**. Dlatego profile **nie** wchodzą jako uśpiony mechanizm.

| Element | MVP (etapy I1–I7) | Później (poza MVP) |
|---|---|---|
| `ImportConfiguration` jako jedyny stan | **tak** — fundament | — |
| `ImportProfileStore` + sekcja w `settings.dat` | **tak** | — |
| **Profil niejawny „ostatnio użyty"** — zapisywany po każdym udanym imporcie, przywracany przy otwarciu zakładki dla tego połączenia | **tak** — to daje ~80% wartości profili od pierwszego dnia i jest precedensowane przez `ParameterHistoryStore`, który sam z siebie stosuje najnowszy zestaw parametrów | — |
| UI listy nazwanych profili: `[Profil ▾] [Zapisz jako…] [Usuń]` w pasku poleceń | **nie** — pusty selektor byłby kłamstwem interfejsu | tak — **wyłącznie widok nad istniejącym magazynem** |
| Eksport/import profilu do `.json` | nie | tak |

**Miejsce w pasku poleceń jest zarezerwowane teraz** (skrajnie lewa pozycja paska B, przed
`Importuj`), żeby dodanie selektora nie przebudowało układu toolbara. Dopóki nazwanych profili nie ma,
przywrócenie ostatniej konfiguracji komunikuje cichy podpis w pasku stanu:
`przywrócono ostatnią konfigurację · [Wyczyść]`.

#### 4.8.5. Wczytanie profilu nie może przemilczeć zmiany (§0.7)

Profil zapisuje **decyzje**, a nie świat. Świat mógł się zmienić: plik zniknął, arkusz ma inne kolumny,
tabela dostała nową kolumnę `NOT NULL`. Dlatego wczytanie profilu przechodzi tę samą, jedną ścieżkę:

```
wczytaj ImportConfiguration
   → odczytaj SourceSchema (jeśli źródło istnieje)      → brak/zmiana ⇒ pozycja w pasku gotowości
   → odczytaj ColumnSpec[] celu (linia Metadata)        → brak tabeli ⇒ pozycja BLOKUJĄCA
   → zastosuj mapowanie przez ImportMappingPlanner z REGUŁĄ ZACHOWANIA DOWODLIWEGO (§4.7)
   → przelicz podgląd i gotowość
```

Kluczowe: **wczytanie profilu nie ma własnej logiki mapowania.** Używa dokładnie tego samego
`ImportMappingPlanner` co ręczna zmiana źródła — jedna odpowiedź na pytanie „czy to mapowanie nadal
jest poprawne", w jednym miejscu.

Dwa wnioski projektowe, które **muszą** wejść już w etapie I1, bo później byłyby przebudową:

1. **`ColumnMapping` identyfikuje pole źródłowe NAZWĄ, gdy nazwa istnieje**, a pozycją tylko wtedy, gdy
   źródło nie ma nagłówka:
   `ColumnMapping { TargetColumnName, SourceFieldName?, SourceFieldIndex, IsSkipped, Origin }`.
   Gdyby mapowanie było wyłącznie pozycyjne, każda zmiana kolejności kolumn w pliku po cichu
   przestawiłaby dane — najgorsza możliwa klasa błędu (§0.1), i naprawa wymagałaby zmiany modelu,
   trwałego formatu **i** UI.
2. **`SourceDescriptor` przechowuje ścieżkę, nie uchwyt**, a `IImportSource` ma `StillExists()` — profil
   musi umieć powiedzieć „ten plik już nie istnieje" bez próby czytania.

#### 4.8.6. Mechanizm, który chroni przed przyszłą przebudową

Sama zasada z §4.8.1 nie wystarczy — musi być **wymuszona**:

- **VM-y sekcji nie posiadają konfiguracji.** `DataImportTabViewModel` trzyma jedną
  `ImportConfiguration`; VM-y sekcji ją czytają i produkują nową (rekord ⇒ podmiana, nie mutacja).
  Jedyne miejsce tłumaczenia „stan UI ⇄ rekord" to para `BuildConfiguration()` / `ApplyConfiguration()`.
- **Test `ImportConfigurationRoundTripTests`** pinuje tożsamość:
  `Apply(Build()) ≡ Build()` oraz `Deserialize(Serialize(c)) ≡ c` dla konfiguracji wypełnionej
  **wszystkimi** polami — plus test refleksyjny sprawdzający, że **każda właściwość
  `ImportConfiguration` bierze udział w obiegu**. Nowe ustawienie dodane bez ujęcia w obiegu **psuje
  build**. To jest ta gwarancja, o którą chodzi w wymaganiu „nie chcę później przebudowywać".
- **Kolejność zapisu ma jedno źródło**: konfiguracja jest zapisywana wyłącznie w koordynatorze
  (po udanym imporcie i przy zamknięciu zakładki), nigdy w VM-ach sekcji — jeden właściciel trwałości.

---

## 5. Diagram przepływu danych

```
                ┌───────────────────────────────────────────────────────────┐
                │  ImportConfiguration  ← JEDEN stan decyzji użytkownika    │
                │  (= treść profilu; §4.8)                                  │
                └───┬──────────────────────────────────────┬────────────────┘
        zapis/odczyt │                                      │ steruje wszystkim
    ┌────────────────▼─────────────┐                        │
    │ ImportProfileStore           │                        │
    │ settings.dat · „ostatnio     │                        │
    │ użyty" + nazwane (później)   │                        │
    └──────────────────────────────┘                        │
                                                            │
   Plik / Schowek ──▶┌──────────────────┐                   │
                     │ IImportSource    │◀──────────────────┤ Source (ścieżka/schowek)
                     │ bajty albo tekst │                   │
                     └────────┬─────────┘                   │
                              │                             │
                     ┌────────▼─────────┐                   │
                     │ IImportProvider  │◀──────────────────┤ Delimited/Spreadsheet options
                     │ ReadSchemaAsync  │──▶ SourceSchema   │
                     │ ReadRecordsAsync │──▶ IAsyncEnum<RawRecord>
                     │ Capabilities     │──▶ co pokazać w sekcji Format
                     └────────┬─────────┘                   │
                              │   ── OD TU WSZYSTKO WSPÓLNE ──
   ColumnSpec[] celu ─▶┌──────▼──────────────────┐          │
   (linia Metadata)    │ ImportMappingPlanner    │◀─────────┤ Mapping (nazwy!)
                       │ auto · reguła pary      │──▶ plan + diagnostyka
                       └──────┬──────────────────┘          │
                              │                             │
                       ┌──────▼──────────────────┐          │
                       │ ImportValueConverter    │◀─────────┤ Culture
                       │ ŚCISŁY — §0.1 / §0.2    │──▶ Ok(value) | Error(kind, raw)
                       └──────┬──────────────────┘          │
                              │                             │
                       ┌──────▼──────────────────┐          │
                       │ ImportRowValidator      │  NOT NULL · długość · skala · charset
                       └──────┬──────────────────┘          │
                              │                             │
              ┌───────────────┴───────────────┐             │
   ┌──────────▼───────────┐     ┌─────────────▼──────────┐  │
   │ DryRunImportWriter   │     │ FirebirdImportWriter   │◀─┤ Transaction, BatchSize,
   │ „Waliduj" — bez zapisu│     │ przygotowany INSERT    │  │ ErrorPolicy, Behavior
   │                      │     │ linia Data · tx usera  │  │
   └──────────┬───────────┘     └─────────────┬──────────┘  │
              └───────────────┬───────────────┘             │
                              │                             │
                       ┌──────▼──────────────────┐          │
                       │ ImportOutcome           │──▶ raport, liczniki, błędy
                       │ + IProgress<…>          │──▶ postęp na żywo
                       └──────┬──────────────────┘          │
                              │                             │
                       ┌──────▼──────────────────┐          │
                       │ Commit / Rollback (user) │  TransactionService
                       └─────────────────────────┘          │
                                                            │
   ┌────────────────────────────────────────────────────────▼────────────────┐
   │ ImportReadiness.Evaluate(configuration, schema, target, txState)        │
   │   → pasek gotowości (§3.2): pozycje blokujące i ostrzegawcze            │
   └─────────────────────────────────────────────────────────────────────────┘

  ── ścieżka boczna, TYLKO dla celu „nowa tabela", PRZED pipeline'em: ──
  próbka RawRecord ─▶ ColumnTypeInferencer ─▶ ImportColumnDefinition[] (w konfiguracji!)
                    ─▶ TableSpec ─▶ DdlGenerator.BuildCreateTable ─▶ FirebirdDdlExecutor
                                                                     (linia Ddl, auto-commit)
```

*(Uwaga do ostatniej ścieżki: profil przechowuje `ImportColumnDefinition[]` — własną, serializowalną
listę definicji kolumn — a `TableSpec` jest z niej budowany dopiero w momencie generowania DDL.
`TableSpec` jest **wejściem generatora**, nie modelem trwałym: ma mutowalną kolekcję tylko do odczytu,
która nie jest przyjazna serializacji. To nie duplikacja, to poprawne rozwarstwienie.)*

---

## 6. Podział na etapy implementacji

Zgodnie z kontraktem sesyjnym EmberTerna: **jeden etap = jedna sesja**, każda kończy się `build 0/0`,
zielonymi testami, czystym smoke testem i commitem.

| # | Etap | Zakres | Definition of Done |
|---|---|---|---|
| **I0** | **Sondy pomiarowe + dokument rekomendacji** *(blokujący, bez kodu produkcyjnego)* | **(a) Sondy.** FB5: przygotowany `INSERT` w pętli vs `FbBatchCommand` (jeśli sterownik go ma) — **zmierzyć, nie wywnioskować**; zachowanie przy błędzie wiersza w środku paczki (czy wiadomo, KTÓRY wiersz padł); koszt commitu co N + próg opłacalności; koszt `CommandLock` per paczka. `.xlsx`: daty jako liczby seryjne (`numFmtId`), shared strings vs inline, puste komórki, wykrywanie ostatniego wiersza, **czy `DocumentFormat.OpenXml` czyta strumieniowo bez materializacji arkusza**. Charset: wstawienie znaku spoza WIN1250 — jaki błąd i **na którym etapie** (klient czy serwer), bo od tego zależy, czy walidacja R1 jest wykonalna lokalnie. **(b) Dokument rekomendacji** `docs/design/data-import-i0-findings.md`: wyniki + jawne stwierdzenie, czy pomiary wymagają zmiany projektu. | Sondy jako **jednorazowe** projekty w `tools/probes/` (poza solution, jak `Fb3ClosureProbe`), uruchomione na bazie laboratoryjnej. Wyniki w §11 (log pomiarów) **oraz** w dokumencie rekomendacji. **Zero kodu produkcyjnego, zero zmian w `src/`.** Werdykt „architektura bez zmian" ⇒ zamrożenie (patrz blok na początku dokumentu); werdykt „wymaga zmiany" ⇒ akceptacja użytkownika przed I1. |
| **I1** ✅ **DOSTARCZONY** | Core: modele + **konfiguracja + magazyn** + czytnik tekstu | `ImportModels`, `ImportOptions`, `ImportEnums`, `ImportContracts`, **`ImportConfiguration`, `ImportProfile`, `ImportProfileStore`**, `DelimitedTextReader` (RFC4180: cudzysłowy, escapowane cudzysłowy, pola wielolinijkowe, CRLF/LF/CR), `DelimiterDetector`, `EncodingDetector`, `FileImportSource`, `TextImportSource`. **`ColumnMapping` z identyfikacją po nazwie (§4.8.5).** | Testy czytnika (≥25 przypadków brzegowych) + **`ImportConfigurationRoundTripTests` wraz z testem refleksyjnym** + `ImportProfileStoreTests`. Zero UI. |
| **I2** ✅ **DOSTARCZONY** | Core: konwersja + mapowanie + walidacja + gotowość | `ImportValueConverter` (ścisły, §0), `ImportMappingPlanner` (auto + reguła pary + diagnostyka `IMP*`), `ImportRowValidator` (+ `ImportCharsetGuard`), **`ImportReadiness`**, oraz dwa fundamenty wynikające z zasady jednego właściciela: `ImportTargetType` (§4.6) i `ImportDiagnostics` (katalog `IMP0001–IMP0027`). | Testy: każdy typ Firebirda × wartość poprawna/niejednoznaczna/błędna; pełna macierz gotowości (blokujące vs ostrzegawcze). Zero cichych konwersji. **Spełnione** — +131 testów, w tym pin odtwarzający samą cichą korupcję charsetu. |
| **I3** ✅ **DOSTARCZONY** | Core: pipeline + dry-run | `ImportPipeline` (wejście: `ImportConfiguration`), `DryRunImportWriter`, `ImportOutcome`, postęp, anulowanie, obie polityki błędów — oraz zaległy z I1 `DelimitedTextImportProvider` (bez niego nie ma end-to-endu na `TextImportSource`). | Testy end-to-end na `TextImportSource` + dry-run. **Pełna funkcjonalność bez bazy i bez UI.** **Spełnione** — +39 testów, w tym pin, że raport nazywa wiersz źródłowy, a nie indeks paczki. |
| **I4** ✅ **DOSTARCZONY** | Firebird: writer + odczyt celu + **weryfikacja na żywo** | `FirebirdImportWriter` — **`FbBatchCommand`, paczki po 500, `MultiError` ustawiany z `ImportErrorPolicy` (I0 §2.3)**, `OVERRIDING SYSTEM VALUE`, `CommandLock` per paczka; `FirebirdImportTargetReader` (kolumny + triggery BEFORE INSERT); mapowanie `FbException` → `ImportErrorKind` **na PARZE kodów GDS, nie na `ErrorCode`** (I0/REK-3: `string truncation` / `numeric overflow` / `transliteration` mają identyczny `ErrorCode` 335544321 i SQLSTATE 22000 — rozróżnia je dopiero **drugi** element wektora: 335544914 / 335544916 / 335544565; wektor obcięcia niesie limit i rzeczywistą długość jako liczby → wprost do raportu; **PK i UNIQUE są nierozróżnialne** (oba 335544665) ⇒ raportujemy „naruszenie unikalności", bez udawania precyzji). **Zero parsowania tekstu komunikatu.** | Import 10 k wierszy do tabeli laboratoryjnej — **liczby zgadzają się z `SELECT COUNT(*)`**; `NOT NULL`, PK/UNIQUE, CHECK, FK, za długi tekst, przekroczenie zakresu, transliteracja, znak spoza charsetu połączenia — **każdy daje właściwy `ImportErrorKind` i właściwy numer wiersza źródłowego**. Przypadki bierzemy z `tools/probes/DataImportWriteProbe` (fazy B/E/C), po czym sonda idzie do usunięcia. |
| **I5** ✅ **ZAMKNIĘTY** (etap + szew domykający, §3.8) | App: zakładka + rama powierzchni + sekcja Źródło i format | `WorkspaceTabKind.DataImport`, `Icon.Import`, przycisk toolbara (D6), near-singleton, dopisanie do listy pomijanej w `SnapshotCurrentTabs`, rama (pasy A–H), zwijalne sekcje, **pasek gotowości**, sekcja Źródło i format z dolną zakładką „Podgląd źródła". | Powierzchnia otwiera plik, pokazuje surowe rekordy i gotowość. Obie palety motywu. |
| **I6** ✅ **ZAMKNIĘTY** | App: sekcja Cel (istniejąca tabela) + panel Mapowanie | Wybór tabeli (`SearchableComboBox`, lista z linii **Metadata**), linia faktów (kolumny · klucz główny · **nazwane** triggery BEFORE INSERT), „opróżnij tabelę przed importem"; siatka mapowania **cel → źródło** z auto-dopasowaniem z `ImportMappingPlanner`, diagnostyką per kolumna, blokadami kolumn systemowych **z powodem**, pochodzeniem w słowniku `ValueOrigin`, listą pól nieużywanych, „Dopasuj po pozycji" / „Wyczyść" / „Tylko niezmapowane"; łańcuch §4.7 rozszerzony: **źródło → cel → mapowanie → gotowość**, anulowalny. | Mapowanie ręczne i automatyczne działa; niezgodności widoczne przed importem. |
| **I7** | App: Podgląd + uruchomienie + raport — **pierwszy pełny przebieg** | Podgląd po konwersji (ciągły), `Waliduj`, tryby transakcji, `Importuj`/`F5`, postęp, anulowanie, raport, Commit/Rollback, eksport raportu, **zapis i przywracanie „ostatnio użytej" konfiguracji**. | **Import CSV → istniejąca tabela działa end-to-end na żywej bazie; druga sesja startuje z przywróconą konfiguracją.** Pierwszy etap z realną wartością dla użytkownika. |
| **I8** | Nowa tabela | `ColumnTypeInferencer` — **(I0/REK-7) domyślnie skanuje CAŁE źródło**, nie próbkę (limit bezpieczeństwa 1 M wierszy), bo w realnym pliku 2 z 5 kolumn były typowo mieszane (R19); siatka typów w sekcji Cel z **zawsze widoczną liczbą przeanalizowanych wierszy** w kolumnie „Podstawa"; podgląd DDL; wykonanie na linii Ddl; ostrzeżenie o nieodwracalności; opcja `DROP` przy niepowodzeniu. | Import do nieistniejącej tabeli działa; typy zachowawcze i edytowalne; DDL z tego samego generatora; **kolumna mieszana ląduje jako `VARCHAR`, nie jako `INTEGER` z bombą zegarową**. |
| **I9** | XLSX + zmiana nazwy projektu (D1) | `EmberTern.Export.Office` → **`EmberTern.Office`**; `XlsxImportProvider`; rozgałęzienie sekcji Format po `Capabilities`. **(I0/REK-6) Siedem wiążących wytycznych providera:** (1) **wyłącznie `OpenXmlReader` (SAX)** — DOM bierze 77× więcej pamięci (R8); (2) wartości umieszczane **po `CellReference`** — brakująca komórka środkowa jest NIEOBECNA, nie pusta, więc czytnik pozycyjny przesunąłby resztę wiersza o kolumnę (§0.1); (3) numer wiersza źródłowego **z `Row.RowIndex`** — puste wiersze są nieobecne, własny licznik skłamałby w raporcie (§0.6); (4) data = liczba + `numFmtId` daty (R3); (5) `SharedStringTable` czytana raz — Excel zapisuje teksty jako shared strings (rozmiar ∝ liczbie RÓŻNYCH tekstów); (6) `SheetDimension` **tylko jako wskazówka** postępu (bywa nieobecny); (7) formuła → wartość zbuforowana, a **komórka błędu → błąd wiersza** (+ opcja `ExcelErrorCellsAsNull`, R20). | Import plików z załączonych zrzutów daje identyczne dane. Eksport XLSX bez regresji. Pierwszy realny plik **z datami** obejrzany (luka pomiarowa R3). Po zamknięciu I9 `tools/probes/DataImportXlsxProbe` idzie do usunięcia. |
| **I10** | Schowek + XLS | Schowek (App czyta, Core parsuje — zero nowego parsera). `XlsImportProvider` + zależność NuGet (D2). | Wklejenie z Excela importuje się bez zapisywania pliku. |
| **I11** | Nazwane profile (UI) | Selektor profili w zarezerwowanym miejscu paska poleceń, `Zapisz jako…`, zmiana nazwy, usuwanie, opcjonalny eksport `.json`. **Zero zmian w modelach i w pipeline.** | Nazwany profil odtwarza cały import; niezgodności raportowane przez pasek gotowości (§4.8.5). |
| **I12** | Domknięcie | Dokumentacja (`docs/history/`, `docs/gotchas.md`, CLAUDE.md w miejscu), audyt UI (obie palety, checklista), pomiar wydajności na 1 M wierszy. | Moduł zamknięty. |

**MVP = I0…I7** (CSV/TXT → istniejąca tabela, z walidacją, raportem i pamięcią ostatniej konfiguracji).
Wszystko dalej jest przyrostowe. **I11 jest dowodem projektu**: jeżeli nazwane profile wymagają zmiany
choćby jednego modelu albo przebudowy sekcji UI, znaczy że §4.8 zostało naruszone po drodze.

---

## 7. Ryzyka

| # | Ryzyko | Prawdop. | Skutek | Postępowanie |
|---|---|---|---|---|
| R1 | 🔴 **ZMIERZONE (I0) — GORZEJ, NIŻ ZAKŁADANO. Charset połączenia CICHO NISZCZY dane.** Znak nieobecny w charsecie **połączenia** (np. cyrylica/CJK przy WIN1250) **nie** wywołuje błędu — jest zapisywany jako `?`, **także gdy kolumna docelowa jest UTF8** (decyduje charset połączenia, nie kolumny). Serwer protestuje tylko w drugą stronę (UTF8 → kolumna WIN1250). | **pewne** (nie „wysokie") | **cicha korupcja danych** — wiersze przechodzą, treść jest uszkodzona; naruszenie reguły #1 | **Walidacja lokalna jest WARUNKIEM §0, nie optymalizacją.** `ImportRowValidator` sprawdza **każdą** wartość tekstową przez `Encoding.GetEncoding(<charset połączenia>)` z **`EncoderExceptionFallback`** (nigdy domyślnym zastępczym — to jedno ustawienie odróżnia „wykryjemy" od „uszkodzimy"); `ImportErrorKind.NotRepresentableInConnectionCharset`; **pozycja ostrzegawcza w pasku gotowości**, gdy charset połączenia ≠ UTF8 a próbka zawiera znaki spoza niego, z podpowiedzią „połącz się w UTF8"; `Waliduj` sprawdza cały plik. |
| R2 | **Niejednoznaczność daty i liczby** (`03.04.2026`; `1,234`) | wysokie | ciche przekłamanie danych — najgorsza klasa błędu | §0.4: kultura jest jawna; auto-detekcja tylko proponuje; **ciągły podgląd po konwersji** pokazuje skutek natychmiast; konwerter odmawia zamiast zgadywać. |
| R3 | **Daty w XLSX to liczby seryjne** (formatowanie w `numFmt`) — **potwierdzone (I0)**: `DataType` jest puste, wartość to numer seryjny, a „datowość" siedzi w `StyleIndex → CellFormat.NumberFormatId` (wbudowane 14–22 / 45–47 albo własny kod z `y`/`d`/`h`/`s`) | wysokie | „2026-07-24" wchodzi jako `46227` | Provider czyta `numFmtId`; przy niejednoznaczności zwraca liczbę **i mówi o tym** w podglądzie. ⚠ **Ograniczenie pomiaru: w pliku użytkownika nie było ANI JEDNEJ komórki daty**, więc obsługa dat jest zaprojektowana na arkuszu wygenerowanym, nie na wyjściu z Excela — pierwszy realny plik z datami trzeba obejrzeć w I9. |
| R4 | **Długa transakcja** przy dużym pliku (własny detektor Session Managera to zgłosi) | średnie | blokady, ryzyko GC dla innych sesji | Tryb `Batched` z jawnym opisem skutku; **pozycja ostrzegawcza w pasku gotowości**, gdy szacowana liczba wierszy > progu (np. 100 k) w trybie jednotransakcyjnym. |
| R5 | **`.xls` (BIFF8) wymaga nowej zależności** | pewne | brak zadeklarowanego źródła w MVP | **D2 zatwierdzone**: poza MVP; przy wyborze `.xls` w I9 komunikat „format nie jest jeszcze obsługiwany — zapisz jako .xlsx". Odmowa z powodem jest zgodna z §0; udawanie obsługi nie. |
| R6 | **Triggery i generatory na tabeli docelowej** — `BEFORE INSERT` może nadpisać wartość, generator „przeskoczy" mimo Rollbacku | średnie | użytkownik nie rozumie wyniku | Sekcja Cel **wypisuje** aktywne triggery `BEFORE INSERT`; pasek gotowości daje pozycję ostrzegawczą; §0.5 mówi wprost, czego Rollback nie cofa. |
| R7 | ✅ **ROZSTRZYGNIĘTE (I0) na korzyść paczek.** `FbBatchCommand` jest **16×** szybszy od pętli przygotowanej (~121 000 vs 7 313 rows/s) **i spełnił warunek blokujący**: podaje indeks błędnego wiersza wyrównany 1:1 z kolejnością dodania (`MultiError=true`), a `MultiError=false` zatrzymuje się **na** błędnym wierszu. Przyjmuje też BLOB-y ⇒ **żadnej ścieżki awaryjnej**. | — | 1 M wierszy: **~8 s** zamiast ~2,3 min | Paczki po 500 (REK-5); `MultiError` mapowany 1:1 na `ImportErrorPolicy`; `CommandLock` per paczka; postęp dławiony. Kontrakt `IImportWriter` skorygowany (REK-1, §4.3). Naiwna pętla (nowa komenda na wiersz) jest 2× gorsza od przygotowanej — nie używamy jej nigdzie. |
| R8 | **Pamięć** — pokusa zmaterializowania pliku, by policzyć wiersze do procentu postępu. **Zmierzone (I0):** odczyt DOM-owy `.xlsx` bierze **300,5 MB** sterty na 100 000 wierszy, SAX-owy **3,9 MB** (**77×**) ⇒ DOM na 1 M wierszy to ~3 GB | średnie | OOM na dużym pliku | Pipeline strumieniowy bez wyjątków; **provider XLSX wyłącznie `OpenXmlReader`** (I9); postęp z **bajtów przeczytanych / rozmiaru** (pliki) albo licznik bez procentu (schowek); `SheetDimension` tylko jako wskazówka. Podgląd trzyma ≤200 wierszy. |
| R9 | **Ciche obcięcie tekstu** (opcja „przytnij") | średnie | utrata danych | Domyślnie wyłączone; przy włączeniu pozycja ostrzegawcza w pasku gotowości + każdy skrócony wiersz w raporcie z oryginałem (§0.2). |
| R10 | **Identity `GENERATED ALWAYS`** — Firebird odrzuca INSERT wymieniający taką kolumnę bez `OVERRIDING SYSTEM VALUE` | średnie | błąd na pierwszym wierszu | Fakt jest w `ColumnSpec.Identity`; mapowanie blokuje kolumnę, a przy jawnym odblokowaniu builder emituje `OVERRIDING SYSTEM VALUE`. |
| R11 | **Kolumny `COMPUTED BY`** — nie da się do nich wstawiać | niskie | mylący błąd serwera | Wykluczone z mapowania z widocznym powodem. |
| R12 | **Konflikt z otwartą transakcją użytkownika** | średnie | import „dołącza się" do cudzej pracy | Pozycja blokująca w pasku gotowości, jak `ResolveRunBlock` Script Executora (spójność zachowań aplikacji). |
| R13 | **Gęstość jednej powierzchni** na małym ekranie | średnie | nieużywalność w oknie 1366×768 | Sekcje zwijalne (i zwijają się same, gdy kompletne), splitter, dolny panel zwijany. Audyt w I12 obejmuje 1366×768. |
| R14 | **„Od czego zacząć"** przy pierwszym użyciu — brak kroków | niskie | dezorientacja | Automatyczne rozwinięcie pierwszej niekompletnej sekcji + pasek gotowości z klikalnymi pozycjami + stany puste z następnym krokiem w treści (§9.4). |
| R15 | **Rozpełzanie konfiguracji po VM-ach** — nowe ustawienie dodane „szybko" tylko do VM | **wysokie** | profile w I11 wymagają przebudowy = dokładnie to, czego użytkownik nie chce | **Test refleksyjny z §4.8.6 psuje build.** To jedyne skuteczne zabezpieczenie; dyscyplina bez testu nie wystarczy. |
| R16 | **Mapowanie pozycyjne w profilu** — zmiana kolejności kolumn w pliku przestawia dane po cichu | średnie | ciche przekłamanie danych | `ColumnMapping` identyfikuje pole **nazwą**, gdy nazwa istnieje (§4.8.5 punkt 1) — decyzja podjęta w I1, nie później. |
| R17 | **Rozdęcie zakresu** (transformacje, UPSERT, filtrowanie — „skoro już tu jesteśmy") | wysokie | moduł nie kończy się nigdy | §9.5: każde rozszerzenie ma wskazany punkt wpięcia i jest **poza v1**; `ImportMode` istnieje od I1, więc dołożenie jest addytywne. |
| R18 | Pusty plik / same nagłówki / jedna kolumna / plik binarny wybrany jako CSV | średnie | wyjątek zamiast komunikatu | Jawne stany puste; provider **zwraca** opisany błąd, nie rzuca. |
| R19 | 🔴 **ZMIERZONE (I0) — kolumny o MIESZANYCH typach są normą, nie wyjątkiem.** W rzeczywistym pliku użytkownika **2 z 5 kolumn** były mieszane: jedna miała 8 723 liczby i **1** tekst, druga 5 805 liczb i 2 919 tekstów. Wnioskowanie z próbki (np. 240 wierszy) zatypowałoby pierwszą jako `INTEGER`, a import padłby na jednym wierszu z 8 724 — **po utworzeniu i ZATWIERDZENIU tabeli** (§4.5), czyli w najgorszym możliwym momencie. | **wysokie** | nieudany import nowej tabeli + osierocona, zatwierdzona tabela | (REK-7) **Wnioskowanie typów domyślnie skanuje CAŁE źródło** (plik i tak jest czytany dwukrotnie: raz do schematu/podglądu, raz do importu), z limitem bezpieczeństwa 1 M wierszy; liczba przeanalizowanych wierszy **zawsze** widoczna w kolumnie „Podstawa"; kolumna mieszana spada do `VARCHAR` (§0.3). Opcja „usuń tabelę, jeśli import się nie powiedzie" nabiera tu realnego znaczenia. |
| R20 | **Komórka błędu Excela** (`#N/A`, `#REF!`) trafiłaby jako **tekst** do `VARCHAR` | średnie | do bazy wchodzi ciąg „#N/A" udający dane | (REK-6) `DataType=Error` jest rozpoznawany i domyślnie daje **błąd wiersza**; addytywna opcja `ImportBehaviorOptions.ExcelErrorCellsAsNull` pozwala świadomie zaimportować je jako NULL. Nigdy cichy tekst. |

---

## 8. Sugestie ulepszeń względem IBExperta

Analiza na podstawie trzech załączonych zrzutów okna *Importuj dane*.

| # | IBExpert | EmberTern | Dlaczego to lepiej |
|---|---|---|---|
| 1 | Cztery gęste zakładki; nic nie prowadzi, a jednocześnie nic nie widać jednocześnie | **Jedna powierzchnia** z sekcjami zwijającymi się do podsumowań + pasek gotowości | Konfiguracja i jej skutek są widoczne razem. Powtarzalny import to `F5`, nie przeklikanie okna. |
| 2 | Podgląd pokazuje **surowe** komórki źródła | Podgląd pokazuje wartości **po konwersji**, przeliczane na żywo | To jedyna informacja, która ma znaczenie: co naprawdę trafi do bazy. Zmiana separatora dziesiętnego natychmiast widać. |
| 3 | Zakładka „Kolumny / Mapowanie" na zrzutach jest **pusta** — bez wyjaśnienia | Mapowanie z auto-dopasowaniem, diagnostyką i **powodem** blokady każdej kolumny systemowej | Pusty ekran bez wyjaśnienia to najczęstsza przyczyna porzucenia importu. |
| 4 | Brak walidacji przed importem | **`Waliduj`** — pełny dry-run bez zapisu (`DryRunImportWriter`) | Można naprawić plik, zanim cokolwiek dotknie bazy. |
| 5 | „Przytnij wartości łańcuchowe jeśli są za długie" — cicha utrata danych jednym checkboxem | Domyślnie **błąd**; przycinanie jawne, opisane skutkiem, ostrzeżenie w pasku gotowości, każdy przypadek w raporcie | §0.2. Cicha utrata danych jest w tym projekcie zakazana. |
| 6 | Model transakcji nieujawniony | Wybór trybu z opisem, jawny Commit/Rollback w raporcie, wprost powiedziane, czego Rollback nie cofnie | Firebirdowy deweloper **musi** wiedzieć, w jakiej transakcji siedzi. |
| 7 | „Ostatni wiersz: 2147483647" (`Int32.MaxValue` wyciekł do UI) | „(do końca)" | Interfejs nie pokazuje szczegółów implementacji. |
| 8 | „Docelowa baza danych" jako combo — w EmberTernie byłoby kłamstwem | Nagłówek pokazuje aktywne połączenie i linię | Jedna prawda: importujemy tam, gdzie jesteśmy połączeni. |
| 9 | Brak paska postępu i anulowania | Postęp, liczniki na żywo, anulowanie z uczciwym stanem transakcji | Import 500 k wierszy bez anulowania jest pułapką. |
| 10 | Raport końcowy: brak | **Raport**: N poprawnych / M błędnych, tabela błędów (wiersz, kolumna, wartość, powód), eksport do CSV/XLSX, podwójne kliknięcie → wiersz w podglądzie | „2 wiersze się nie zaimportowały" bez wskazania których jest bezużyteczne. |
| 11 | Auto-wykrywanie: brak | Auto-detekcja separatora i kodowania **z pokazaną podstawą** („240/240 wierszy ma tę samą liczbę kolumn") | Automat, który tłumaczy swoją decyzję, buduje zaufanie; milczący — nie. |
| 12 | Nowa tabela: typy wybierane „jakoś" | Wnioskowanie zachowawcze + kolumna **„Podstawa"** + edytowalne + podgląd DDL z tego samego generatora co reszta aplikacji | Użytkownik widzi, *dlaczego* dostał `VARCHAR(20)`, i może to zmienić przed wykonaniem. |
| 13 | „Skojarz kolumny wg nazw" jako checkbox, wynik niewidoczny | Auto-mapowanie po nazwie domyślne, wynik w siatce, z oznaczeniem pochodzenia (dopasowane / założone / ręczne) | Widać, co zrobił automat, i można to punktowo poprawić. |
| 14 | Ostrzeżenia o identity / computed / triggerach: brak | Jawne, przed importem, w pasku gotowości | Klasyczne „dlaczego mój import padł na pierwszym wierszu". |
| 15 | Zapis konfiguracji: przez IBEBlock (czyli: napisz skrypt) | **Konfiguracja jest trwała od MVP** („ostatnio użyta"), nazwane profile to widok nad tym samym magazynem (I11) | Powtarzalny import bez pisania skryptu — i bez obietnicy „kiedyś dobudujemy". |

---

## 9. Rekomendacje dotyczące UX

### 9.1. Zasady naczelne

1. **Powierzchnia pokazuje stan, nie proces.** W każdej chwili widać całą konfigurację i to, czego
   brakuje. Nie ma „gdzie jestem".
2. **Nic nie zaskakuje po `Importuj`.** Wszystko, co może pójść źle, jest widoczne wcześniej — pasek
   gotowości (przewidywanie) + podgląd po konwersji (dowód) + `Waliduj` (pełny sprawdzian). Import ma
   być nudny.
3. **Każde „nie da się" mówi dlaczego** i prowadzi do winnego miejsca jednym kliknięciem. Wyszarzony
   przycisk bez powodu jest błędem UX — konwencja `ExportUnavailableReason` obowiązuje tu tak samo.
4. **Liczby zamiast przymiotników.** Nie „duży plik", lecz „1 240 wierszy"; nie „możliwe problemy", lecz
   „3 wiersze przekroczą limit długości".
5. **Jedna powierzchnia komunikatów** — wyłącznie `MessageBanner`. Zero lokalnie kolorowanych napisów.
6. **Druga sesja jest tańsza niż pierwsza.** Konfiguracja wraca sama; jedyną czynnością bywa wskazanie
   nowego pliku.

### 9.2. Klawiatura (spójnie z resztą aplikacji)

| Skrót | Działanie |
|---|---|
| `F5` | Importuj — spójnie z „F5 = wykonaj" w całym EmberTernie |
| `Ctrl+F5` | Waliduj (dry-run) |
| `Ctrl+O` | Wybierz plik |
| `Ctrl+V` | Użyj schowka jako źródła (gdy fokus nie jest w polu tekstowym) |
| `Esc` | Anuluj trwający import; poza tym nic nie zamyka zakładki przypadkiem |
| `F6` | Kolejna sekcja / panel (Źródło → Cel → Mapowanie → Podgląd) — nawigacja bez myszy |
| `Ctrl+1..4` | Rozwiń i fokusuj konkretną sekcję |

Po otwarciu zakładki fokus ląduje na pierwszym polu decyzyjnym pierwszej niekompletnej sekcji (wzorzec
z D15.3 Seam C).

### 9.3. Język wizualny

- **Zero nowych kolorów** poza ewentualnym jednym tokenem „kolumna niezmapowana" (najpierw sprawdzić
  `SubtleForegroundBrush` + kursywę — wystarczyły w debuggerze dla `Restored`).
- Pochodzenie mapowania: `Restored` = cicha kursywa (`SubtleForegroundBrush`), `Assumed` = `AccentBrush`
  półgruby + słowo „założone" (**akcent, nie ostrzeżenie**), `Manual` = zwykły tekst.
- Pasek gotowości używa **tej samej** mapy `Severity` → pędzel/geometria co `MessageBanner`
  (`BrushKeyFor`/`GeometryKeyFor`), żeby pas i banner nie mogły się rozejść.
- Ikony wyłącznie z systemu `SvgIcon`; jedna nowa (`Icon.Import`), reszta istniejąca (`Icon.Play`,
  `Icon.Stop`, `Icon.Check`, `Icon.Undo`, `Icon.Table`, `Icon.RefreshCw`, `Icon.Folder`, `Icon.Save`).

### 9.4. Stany puste i błędne

Każdy obszar ma zaprojektowany stan pusty z **następnym krokiem w treści**, nie samym „brak danych":
- Źródło: „Wybierz plik albo wklej dane ze schowka."
- Cel: „Ta baza nie ma jeszcze tabel — wybierz »Nowa tabela«."
- Mapowanie: „Żadna nazwa kolumny nie pasuje — dopasuj ręcznie albo użyj »Dopasuj po pozycji«."
- Podgląd: „Wszystkie wiersze przechodzą walidację." (sukces też jest stanem wartym pokazania)
- Raport: „Jeszcze nie uruchomiono importu. `Waliduj` sprawdzi dane bez zapisu."

### 9.5. Punkty wpięcia przyszłych rozszerzeń

| Rozszerzenie | Gdzie się wpina | Co musi istnieć **już teraz** |
|---|---|---|
| **Nazwane profile importu (I11)** | UI listy nad `ImportProfileStore`; zarezerwowane miejsce w pasku poleceń | **`ImportConfiguration` + `ImportProfileStore` + test obiegu — powstają w I1** ✔ |
| Eksport/import profilu do `.json` | serializacja tego samego rekordu | jeden rekord bez typów UI ✔ |
| Transformacje kolumn i kolumny wyliczane | krok między 2 i 3 pipeline'u: `IImportValueTransform` przed konwersją; opcje w `ImportConfiguration` | pipeline z **jednym jawnym punktem przejścia wartości** ✔ |
| UPSERT / UPDATE / INSERT ONLY / MERGE | `ImportMode` + drugi `IImportWriter` (albo strategia budowania DML) | `ImportMode` **istnieje od I1** z jedyną wartością `Insert`; niesie go konfiguracja ✔ |
| Filtrowanie danych przed importem | predykat na strumieniu `RawRecord`; `GridFilter` z Core nadaje się wprost | pipeline strumieniowy + `GridFilterEvaluator` ✔ |
| Import wielu arkuszy / wielu plików | pętla nad `IImportSource` / `SheetIndex` w koordynatorze; pipeline bez zmian | `IImportSource` jako port **osobny** od providera ✔ |
| Rozpoznawanie domen przy wnioskowaniu typów | rozbudowa `ColumnTypeInferencer` | inferencer jako **osobna klasa czysta**, nie kod w VM ✔ |
| Własne konwertery typów | rejestr w `ImportValueConverter` (typ docelowy → konwerter) | konwerter z jawnym wejściem `(raw, ColumnSpec, culture)` ✔ |
| Import z wyniku zapytania / baza→baza | kolejny `IImportProvider` nad `QueryResult` | kontrakt providera nie zakłada pliku ✔ |

**Kluczowa własność:** żadne z powyższych nie wymaga przebudowy pipeline'u, modeli, formatu trwałego ani
ramy UI — tylko dołożenia implementacji istniejącego portu, jednego kroku w istniejącym łańcuchu albo
widoku nad istniejącym magazynem.

---

## 10. Decyzje — **ZATWIERDZONE 2026-07-26**

| # | Decyzja | Ustalenie |
|---|---|---|
| **D1** | Gdzie żyje kod Excela | ✅ **`EmberTern.Export.Office` → `EmberTern.Office`**, oba kierunki w jednym projekcie (nazwa opisuje odpowiedzialność, nie historię). Zmiana wykonywana w etapie I9, razem z pierwszym konsumentem. |
| **D2** | Czy `.xls` (BIFF8) wchodzi do MVP | ✅ **Nie.** MVP obejmuje `.xlsx`. `.xls` w I10 wraz z decyzją o zależności (`ExcelDataReader`, MIT). Do tego czasu wybór `.xls` daje komunikat „zapisz jako .xlsx" — odmowa z powodem, nigdy udawanie obsługi. |
| **D3** | Domyślny tryb transakcji | ✅ **`Manual`** — transakcja pozostaje otwarta, użytkownik decyduje (reguła #3, spójność ze Script Executorem). |
| **D4** | Domyślna polityka błędów | ✅ **`StopOnFirstError`**. „Pomiń błędne wiersze" jest świadomym wyborem, nie przypadkiem. |
| **D5** | „Opróżnij tabelę przed importem" | ✅ **Tak**, jako `DELETE FROM` w **tej samej** transakcji roboczej (wycofywalne), z potwierdzeniem pokazującym aktualną liczbę rekordów. Nigdy obejście typu `TRUNCATE`. |
| **D6** | Umiejscowienie modułu | ✅ **Główny toolbar, obok Script Executora**, aktywny tylko przy połączeniu (wzorzec `CanOpenScriptExecutor`). |
| **D7** | Kierunek UI | ✅ **Jedna powierzchnia robocza z sekcjami** (nie kreator). Uzasadnienie: §1.2. `BreadcrumbBar` nie jest używany. |
| **D8** | Zakres profili | ✅ **Architektura i trwałość w MVP** (`ImportConfiguration` + `ImportProfileStore` + niejawny profil „ostatnio użyty", wszystko żywe od I7); **UI nazwanych profili w I11** jako czysty widok nad tym samym magazynem. Miejsce w pasku poleceń zarezerwowane od I5. |
| **D9** | Kontrakt `IImportWriter` po pomiarach I0 (REK-1) | ✅ **Skorygowany i przyjęty** (2026-07-26): `WriteAsync` przyjmuje wiersz do bieżącej paczki, `FlushBatchAsync` wykonuje zapis i zwraca wynik **każdego** elementu paczki, a **pipeline** utrzymuje mapowanie „indeks w paczce → numer wiersza źródłowego" i na tej podstawie buduje raport (§4.3). Podstawa: paczka jest 16× szybsza **i** spełniła warunek poprawnej identyfikacji błędnych wierszy. |
| **D10** | Pozostałe rekomendacje I0 | ✅ **Wszystkie przyjęte** (2026-07-26): walidacja charsetu z `EncoderExceptionFallback` jako obowiązkowy element pipeline'u (REK-2) · mapowanie błędów na wektorze GDS, nie `ErrorCode` (REK-3) · wnioskowanie typów na całym źródle (REK-7) · XLSX wyłącznie SAX (REK-6) · `Batched` zostaje, bo jego koszt jest pomijalny (REK-5) · przycinanie tylko jako opt-in (REK-4) · `.xls` poza MVP (REK-8). Szczegóły i liczby: [data-import-i0-findings.md](data-import-i0-findings.md). |

---

## 11. Zgodność z zasadami EmberTerna — lista kontrolna

| Zasada | Jak spełniona |
|---|---|
| **Core First** | Pipeline, konfiguracja, konwersja, mapowanie, wnioskowanie typów, gotowość i providerzy tekstowi są w Core. Etapy I1–I3 dają **kompletną funkcjonalność bez bazy i bez UI**. |
| **Single Source of Truth** | Zero nowego generatora DDL, modelu kolumny, mechanizmu transakcji, serializacji eksportu, wzorca siatki i powierzchni komunikatów (§4.6, 17 punktów). **Jedna reprezentacja konfiguracji** (§4.8) — i test, który tego pilnuje. |
| **UX First** | Jedna powierzchnia, ciągły podgląd po konwersji, dry-run, pasek gotowości z klikalnymi powodami, raport z przyczynami, druga sesja tańsza od pierwszej. |
| **Rozszerzalność** | Trzy porty z ≥2 implementacjami; 9 rozszerzeń ma wskazany punkt wpięcia (§9.5); **I11 jest testem tej własności** — nazwane profile nie mogą wymagać zmiany modeli. |
| **Brak duplikacji logiki** | Jeden pipeline dla wszystkich źródeł; schowek nie jest osobnym parserem; dry-run to ten sam przebieg z innym writerem; wczytanie profilu używa tego samego `ImportMappingPlanner` co ręczna zmiana źródła. |
| **Łatwość testowania** | Core czysty → testy jednostkowe; `DryRunImportWriter` daje testy end-to-end bez bazy; `ImportReadiness` i `ImportConfiguration` są czystymi funkcjami/rekordami; `DataImportProbe` weryfikuje warstwę Firebirda na żywym silniku. |
| **Reguła #1** (Core bez Avalonia) | Core nie zna schowka, okien dialogowych ani pędzli; App podaje `string`/ścieżkę. |
| **Reguła #2** (brak interfejsów z jedną implementacją) | 3 porty × ≥2 implementacje produkcyjne (§4.3). |
| **Reguła #3** (nigdy auto-commit) | Domyślnie transakcja pozostaje otwarta (D3); `AutoCommitOnSuccess` i `Batched` są jawnym wyborem z opisem skutku. |
| **Reguła #4** (wirtualizowane siatki) | Podgląd, mapowanie i raport na standardowym `DataGrid` z istniejącym wzorcem. |
| **Reguła #6** (`UiStrings`) | Core zwraca kody (`ImportErrorKind`, `IMP*`, `ReadinessItem.Code`); wszystkie napisy w `UiStrings`. |
| **Reguła #9** (motywy) | Wszystkie kolory z tokenów, obie palety, checklista UI w I12. |
| **Reguła #11 / §0** | Rozpisana na siedem wiążących konsekwencji na początku dokumentu. |
| **„Weryfikuj Firebirda, nie wnioskuj"** | ✅ **Wykonane w I0.** Dwie sondy zmierzyły silnik i biblioteki; **trzy założenia dokumentu zostały skorygowane pomiarem** (R1 z „odrzucone wiersze" na cichą korupcję, R7 rozstrzygnięte na korzyść paczek, mapowanie błędów z `ErrorCode` na wektor GDS) i **jedno ryzyko powstało z danych użytkownika** (R19 — kolumny mieszane). #213 było uwzględnione w modelu transakcyjnym od v1 (§4.5). I4 kończy się weryfikacją na bazie laboratoryjnej. |
| **Gotcha #233** (nie zostawiaj uśpionych komponentów) | Magazyn profili **jest używany od I7** przez profil „ostatnio użyty" — nic nie leży martwe do I11. |
| **Gotcha #240** (splitter + zwijanie panelu) | Dolny panel zwija się przez **pełną renormalizację obu wierszy** siatki, nie przez zmianę jednego. |

### Log pomiarów — etap I0, **wykonany 2026-07-26**

Środowisko: Firebird **WI-V5.0.3.1683** (`localhost:3050`) · `FirebirdSql.Data.FirebirdClient 10.3.4` ·
`DocumentFormat.OpenXml 3.1.0` · .NET 9. Sondy: `tools/probes/DataImportWriteProbe`,
`tools/probes/DataImportXlsxProbe` (baza tymczasowa w `C:\Temp`, usuwana; lab nietknięty).

**Zapis (50 000 wierszy, 6 kolumn):**

| Wariant | rows/s |
|---|---:|
| naiwny (nowa komenda na wiersz, bez `Prepare`) | 3 586 |
| przygotowana komenda + re-bind, 1 tx | 7 313 |
| … + PK i indeks na tabeli | 6 738 |
| … commit co 10 000 / 1 000 / 100 | 7 089 / 7 141 / 6 983 |
| **`FbBatchCommand`, paczki po 500** | **~121 000** |
| `FbBatchCommand`, paczki po 1 000 / 2 000 / 5 000 / 20 000 | 116 896 / 89 800 / 65 165 / 33 048 |

- **Commit jest praktycznie darmowy** (co 100 wierszy = −4,5%) ⇒ `Batched` nie ma ceny wydajnościowej.
- **Optimum paczki 250–1 000, rekomendacja 500**; powyżej 2 000 wydajność spada liniowo.
- **`CommandLock` per paczka: poniżej progu mierzalności** (delta mieści się w szumie ~4% między
  przebiegami).
- **BLOB przechodzi przez paczkę** bez straty (20 000 znaków, round-trip OK) — ścieżka awaryjna zbędna.

**⭐ Atrybucja błędnego wiersza w paczce — ODPOWIEDŹ POZYTYWNA** (warunek blokujący R7): przy
`MultiError=true` wynik ma `Count` == rozmiar paczki i nieudany element stoi pod **tym samym indeksem**,
pod którym wiersz został dodany (1:1). Przy `MultiError=false` paczka **zatrzymuje się na** błędnym
wierszu (`Count` = liczba prób). `MultiError` odwzorowuje się 1:1 na `ImportErrorPolicy`
(`false`→`StopOnFirstError`, `true`→`SkipInvalidRows`).

**⭐ Kody błędów — mapowanie musi czytać WEKTOR GDS, nie `ErrorCode`:** `NOT NULL`=335544347 ·
PK i UNIQUE=**335544665 (nierozróżnialne)** · CHECK=335544558 · FK=335544466; natomiast **tekst za długi,
przekroczenie zakresu liczby i błąd transliteracji mają identyczny `ErrorCode` 335544321 i SQLSTATE 22000**,
a rozróżnia je **drugi** element wektora (335544914 / 335544916 / 335544565). Wektor obcięcia niesie limit
i rzeczywistą długość jako liczby.

**Tekst za długi:** odrzucany przez serwer, **nigdy cicho obcinany**.

**⭐⭐ Charset połączenia — POTWIERDZONA CICHA PODMIANA:** przy połączeniu WIN1250 znak `Ж`/`中` jest
**zapisywany jako `?` bez żadnego błędu** — także wtedy, gdy kolumna docelowa jest UTF8 (decyduje charset
**połączenia**, nie kolumny). Serwer protestuje tylko w drugą stronę (połączenie UTF8 → kolumna WIN1250,
GDS 335544565). ⇒ walidacja lokalna jest **warunkiem §0**, nie optymalizacją.

**Odczyt `.xlsx`:**

- **Strumieniowo obowiązkowo:** `OpenXmlReader` = 3,9 MB sterty vs DOM = 300,5 MB dla 100 000 wierszy /
  500 000 komórek (**77×**), czasy 1,97 s vs 2,52 s.
- **Brakująca komórka środkowa jest NIEOBECNA, nie pusta** ⇒ wartości umieszczane po `CellReference`,
  nigdy pozycyjnie (inaczej ciche przesunięcie kolumn).
- **Puste wiersze są nieobecne** ⇒ numer wiersza źródłowego z `Row.RowIndex`, nigdy z własnego licznika.
- Data = liczba + `numFmtId` daty (wbudowane 14–22/45–47 lub własny kod z `y`/`d`/`h`/`s`); inline string
  ma **`CellValue` = null** (tekst w `InlineString/Text`); formuła niesie wartość zbuforowaną; komórka
  błędu ma `DataType=Error`.
- Plik z **prawdziwego Excela** (8 724 wiersze × 5 kolumn): teksty jako **shared strings** (8 261 różnych),
  `SheetDimension` obecny (`A1:E8724`), **0 komórek daty**, i — istotne — **2 z 5 kolumn są typowo
  mieszane** (B: 8 723 liczby + 1 tekst; E: 5 805 liczb + 2 919 tekstów).
- **D2 potwierdzone:** OpenXml odrzuca `.xls` (`FileFormatException`).

Pełny opis, interpretacja i **8 rekomendacji** (w tym jedna wymagająca decyzji — semantyka
`IImportWriter`) mieszkają w **[data-import-i0-findings.md](data-import-i0-findings.md)**. Ten log jest
skrótem liczb, żeby sesja implementacyjna nie musiała otwierać drugiego dokumentu dla samych pomiarów.

---

## 12. Czego moduł świadomie NIE robi w MVP

Każda pozycja ma powód i wskazane miejsce, w którym zostanie dołożona:

- **UPSERT / UPDATE / MERGE** — MVP to `INSERT`. `ImportMode` istnieje od I1, dołożenie jest addytywne.
- **Transformacje i kolumny wyliczane** — osobny krok pipeline'u, świadomie poza zakresem (R17).
- **Filtrowanie danych przed importem** — dlatego panel filtrów **nie jest** podpięty do podglądu
  (§3.6); filtr, który nie wpływa na import, byłby mylący.
- **UI nazwanych profili** — I11; architektura i trwałość są w MVP (D8).
- **`.xls`** — I10 (D2).
- **Wiele plików / wiele arkuszy naraz** — pętla w koordynatorze, pipeline gotowy.
- **Import do widoku modyfikowalnego** — IBExpert to oferuje; wymaga sprawdzenia, czy widok jest
  aktualizowalny (trigger `INSTEAD OF`), więc to osobna, weryfikowana funkcja, nie dopisek.
- **Harmonogram / import z linii poleceń** — poza filozofią produktu (workbench, nie ETL).
