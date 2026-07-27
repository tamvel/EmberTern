# Data Import — dokument projektowy modułu importu danych

**Status: 🔒 PROJEKT ZAMROŻONY + AMENDMENT I7.5. 🏁 WSZYSTKIE ETAPY I0–I12 DOSTARCZONE (2026-07-27);
I12 — domknięcie — oczekuje potwierdzenia użytkownika. Architektura po poprawce I7.5 jest obowiązująca.**
Następny krok: **decyzja użytkownika o scaleniu `feat/data-import` do `master`** — nie ma kolejnego etapu.
**Narracja modułu wyprowadzona do [docs/history/21-data-import.md](../history/21-data-import.md)**
(etap I12; ~520 wierszy zeszło z CLAUDE.md). Ten dokument zostaje **architekturą**; historia mieszka tam.
Poza modułem świadomie zostają: **U4** (gęstość kontrolek → osobny sprint UX całego EmberTerna **po**
zamknięciu modułu, decyzja użytkownika) oraz pozostałe pozycje z tabeli „Co zostaje otwarte" w pliku
historii, każda z powodem. **U5 został ZAMKNIĘTY** w trzecim przeglądzie I11 (§3.8).
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
| **Ostatni commit** | etap **I8** — `b95af9a`, + **poprawka awarii z przeglądu `9f2817a`** (poprzedni: `dc653b1` — domknięcie MVP I0–I7.5). **Wypchnięte na OBA remote'y.** ℹ️ Przy pierwszym zamknięciu etapu `origin` był nieosiągalny (DNS nie rozwiązywał hosta — maszyna poza siecią firmową), a `private` przeszedł: dokładnie ta izolacja awarii, dla której odrzucono wariant z dwoma `pushurl`. Dosłane po powrocie do sieci |
| **Etapy zamknięte** | **I0** (sondy, `5e90435`) · **I1** (modele, konfiguracja, magazyn, czytnik, `77eb997`) · **I2** (konwersja, mapowanie, walidacja, gotowość, `392850f`) · **I3** (pipeline + dry-run + provider, `434daeb`) · **I4** (Firebird + weryfikacja na żywym FB5, `3b31a4d`) · **I5** (`0c5667e` + szew `95ae39e`) · **I6** (`4f2de74`) |
| **✅ I5 — ZAMKNIĘTY (2026-07-26)** | Przegląd wzrokowy dał 5 uwag (U1–U5) + 5 propozycji z autoprzeglądu (U6–U10) + U11 + U12 z drugiego oglądu. **Wszystkie rozstrzygnięte; 10 dostarczonych w szwie domykającym** (§3.8). Układ **zrewidowany i wniesiony w miejsce do §3.1** — gwiazdka na powierzchni roboczej, pas A usunięty, kafelki pionowe z zawsze żywym pickerem, grupy ustawień jako karty. **Układ zaakceptowany przez użytkownika.** Otwarte świadomie: **U4** (gęstość globalna → sprint UX po module) i **U5** (weryfikacja przy I6) |
| **✅ I6 — ZAMKNIĘTY (2026-07-26)** | Sekcja **Cel** (istniejąca tabela) + panel **Mapowanie** + łańcuch przeliczeń rozszerzony o cel i mapowanie. Potwierdzony wzrokowo przez użytkownika; odstępstwo o `COUNT(*)`, nazwy triggerów, orientacja „cel → źródło" i reguły identity — **zaakceptowane bez zmian** |
| **✅ I7 — ZAMKNIĘTY I ZAAKCEPTOWANY (2026-07-26)** | Podgląd po konwersji + pasek poleceń (`Importuj`/F5, `Waliduj`/Ctrl+F5, `Anuluj`/Esc, tryb transakcji, polityka błędów, `ExecutionTimer`) + uruchomienie z postępem i anulowaniem + zakładki **Błędy** i **Raport** + Commit/Rollback w raporcie + eksport raportu przez istniejący framework + „ostatnio użyta" konfiguracja + domknięta zaległość I6 (liczba rekordów przy „opróżnij tabelę"). **KONIEC MVP** |
| **✅ I7.5 — ZAMKNIĘTY I ZAAKCEPTOWANY (2026-07-26)** | Data Import ma **własną transakcję na własnym przyłączeniu** (amendment §4.5): wspólny `FirebirdSessionConnection` wydzielony z Debuggera przez kompozycję, `ImportSessionConnection`, `PendingWorkRegistry` jako **jedyny właściciel** pytania o niezatwierdzoną pracę. Sonda **13/13 ALL PASS** |
| **✅ I8 — DOSTARCZONY (2026-07-27), oczekuje potwierdzenia wzrokowego** | Nowa tabela: `ColumnTypeInferencer` (skan CAŁEGO źródła), `ImportNewTable` (jedyny właściciel „definicja → SQL"), edytowalna siatka typów z kolumną „Podstawa", podgląd DDL, `CREATE` na linii **Ddl** przed pierwszym wierszem, `DROP` przy niepowodzeniu, `IMP0028` |
| **🔴 I8 — POPRAWKA PO PRZEGLĄDZIE (2026-07-27): „Importuj" na nowej tabeli ZAMYKAŁO APLIKACJĘ** | Zgłoszone przez użytkownika; **dwa niezależne defekty, oba w I8**, oba naprawione i zapinowane. Szczegóły w bloku niżej |
| **✅ I8 — DOMKNIĘCIE PO PRZEGLĄDZIE (2026-07-27): nowa tabela pojawia się w drzewie od razu** | Drugie zgłoszenie z ręcznego QA: import przechodził, tabela powstawała, dane wchodziły — ale **Explorer metadanych jej nie pokazywał** do ręcznego odświeżenia. Naprawione **bez 21. wywołania `RefreshAsync()`**: moduł zgłasza fakt (`DataImportEnvironment.TableCreated` / `TableDropped`), a drzewo wstawia/usuwa **jeden liść w miejscu**. Przy okazji, w tej samej sesji, **Warstwa 1** z raportu — patrz blok niżej |
| **✅ I9 — ZAMKNIĘTY I ZAAKCEPTOWANY (2026-07-27)** | XLSX: `EmberTern.Export.Office` → **`EmberTern.Office`** (D1), `XlsxImportProvider` (7 wytycznych REK-6), rozgałęzienie sekcji Format po **`Capabilities`**, `SourceErrorValue` domykający R20. ⭐ **Filar „jeden pipeline dla każdego źródła" utrzymał się: pipeline, konwerter, walidator, mapowanie i writer NIE zostały zmienione.** Potwierdzone wzrokowo przez użytkownika w obu paletach — sprawdzone osobno: wybór arkusza, ukrywanie ustawień właściwych dla CSV/TXT, opcja „traktuj komórki dat jako daty" i pełny przebieg importu XLSX; bez uwag wizualnych i funkcjonalnych |
| **✅ I10 — DOSTARCZONY (2026-07-27), oczekuje potwierdzenia wzrokowego** | Schowek + `.xls` (BIFF8). ⭐ **Filar utrzymał się po raz drugi, tym razem pod większym obciążeniem — bo doszła NOWA ZALEŻNOŚĆ NuGet, a mimo to pipeline, konwerter, walidator, mapowanie i writer znów nie zostały tknięte.** Schowek okazał się już zbudowany (I5 dał przełącznik i pole, `MainWindow` czytanie ze schowka) — etap dołożył mu wyłącznie dowody. Szczegóły w bloku „⭐ I10 as-built" |
| **🏁 SZEW ERGONOMICZNY PO I10 — ZAMKNIĘTY I ZAAKCEPTOWANY (2026-07-27)** | Trzy uwagi użytkownika z przeglądu I10, potraktowane jako domknięcie I10, **nie** jako nowy etap: schowek jako **źródło żywe**, przycisk **Odśwież**, i wymóg, żeby ponowny odczyt przechodził **tym samym łańcuchem** co pierwszy. **Potwierdzone przez użytkownika: bez uwag.** Wprost zaakceptowany kierunek architektoniczny — *„najważniejsze, że nie powstała druga ścieżka odświeżania, tylko wszystko przechodzi przez jeden łańcuch z różnymi powodami uruchomienia (Decision / Refresh)"*. `Ctrl+R` zostaje, **`F5` świadomie NIE dodane** (decyzja użytkownika — `F5` to Import). Szczegóły w bloku „⭐ Szew ergonomiczny po I10" |
| **✅ I11 — ZAMKNIĘTY I ZAAKCEPTOWANY (2026-07-27)** | Nazwane profile. ⭐⭐ **Rachunek §4.8 się zgodził: ANI JEDEN model nie został zmieniony i pipeline nie został tknięty.** `ImportConfiguration`, `ImportProfile`, `ImportPipeline`, konwerter, walidator, planer mapowania i writer — bez jednej linii różnicy. Wczytanie profilu to `ApplyConfiguration` i **nic więcej**, więc profil niezgodny ze światem melduje się w pasku gotowości (IMP0011 / IMP0016), a nie wyjątkiem. Selektor wszedł w **zarezerwowane** miejsce paska B — układ toolbara nie został przebudowany. Szczegóły w bloku „⭐ I11 as-built" |
| **🔁 I11 — SZEW PO PRZEGLĄDZIE (2026-07-27)** | Dwie uwagi użytkownika z oglądu UI, obie zasadne, obie o **wyjściu** z profilu: (1) nie dało się wrócić do pracy **bez** profilu — doszła stała pozycja **„(no profile)"** na czele listy, która **odłącza i ZOSTAWIA decyzje**; (2) nie było jak wyczyścić powierzchni — doszedł przycisk **`Reset`**, który przywraca domyślne **i** odłącza profil. To są świadomie **dwie różne akcje**, nie jedna. Plus trzecia uwaga: **wyraźniejsze oddzielenie** grupy profili od grupy wykonania. Szczegóły w bloku „⭐ I11 as-built" |
| **⛔ DYREKTYWA UŻYTKOWNIKA PRZY ZAMYKANIU I11 (2026-07-27)** | **Nie wracamy już do kosmetyki Data Import.** Użytkownik zamknął etap ze świadomym stwierdzeniem, że nie wszystko wygląda docelowo, ale pozostałe uwagi są **czysto UX** i naturalnie należą do planowanego **globalnego sprintu UX** całej aplikacji (wymiana kontrolek Avalonia, zagęszczenie, ujednolicenie zachowań). Do modułu wracamy **wyłącznie przy rzeczywistym błędzie funkcjonalnym** |
| **🔁 I11 — DRUGI PRZEGLĄD (2026-07-27)** | Pięć uwag o ergonomii powierzchni, wszystkie w granicach modułu. ⭐ Dwie okazały się **defektem, nie kwestią gustu**: chipy `Target` i `Mapping` naprawdę nic nie robiły w zwyczajnych stanach, bo celowały w kontrolkę wyłączoną albo w wiersz, którego nie ma. Poza tym: usunięte zdublowane ostrzeżenie §0.5, `Existing/New table` w jednej siatce ze wspólnymi kolumnami, kolumna `Basis` przycięta z podpowiedzią, tytuł panelu Mapowanie. Szczegóły w bloku „⭐ Drugi przegląd I11" |
| **🔁 I11 — TRZECI PRZEGLĄD (2026-07-27), przygotowanie do scalenia** | Cztery poprawki wybrane przez użytkownika z audytu; **żadna nie tknęła modeli ani pipeline'u.** ⭐ **U5 DOMKNIĘTY** — wiersz roboczy dostał podłogę (`MinHeight`) i, co ważniejsze, **zacisk, który tę podłogę czyni osiągalną**: `ApplyBottomPanel` przycina dolny panel, bo z dwóch pasów walczących o resztę miejsca to jego wysokość wybraliśmy my, a nie treść. Do tego: chip `Transaction` otwiera listę (jedyna sekcja, która nie jest pasem powierzchni, więc `BringIntoView` był tam zawsze pusty), wspólna „Podstawa" mówiona **raz dla sekcji** zamiast raz na wiersz, oraz `ImportReadinessReport.Prioritized` — sufit paska nie może już schować błędu blokującego za ostrzeżeniem. Szczegóły w bloku „⭐ Trzeci przegląd I11" |
| **🏁 I12 — DOSTARCZONY (2026-07-27), oczekuje potwierdzenia użytkownika** | Domknięcie: narracja modułu do **[docs/history/21-data-import.md](../history/21-data-import.md)** (~520 wierszy zeszło z CLAUDE.md, który skurczył się o 490 wierszy) · **audyt UI w obu paletach** — zero twardych kolorów, zero lokalnych pędzli, zero `{StaticResource}` na pędzlu, zero lokalnych stylów, 13/13 tokenów w obu paletach, **przypięte testem** żeby audyt był powtarzalny · ⭐ **pomiar na 1 M wierszy**: Manual **14,0 s / 71 437 wierszy/s**, sterta **płaska** (~1 MB przez cały przebieg), Batched 19,6 s i **990 000 z 999 997 przeżywa Rollback** — domyślne I0 (500 / 10 000) **bronią się** · raport uczciwy przy tej skali (numery rekordów, sufit błędów, licznik postępu) · **ostatnia poprawka funkcjonalna**: nieświeża lista tabel po `CREATE` (zgłoszenie dotyczyło pickera; groźniejsza połowa to `IMP0028`, które przestawało widzieć zajętą nazwę) · jawna lista tego, co zostaje otwarte, z powodem przy każdej pozycji |
| **Następny krok** | **decyzja użytkownika: scalenie `feat/data-import` do `master`.** Nie ma kolejnego etapu |
| **Testy** | **5846 zielonych**, 0 niepowodzeń (I11 dodał **+45**: 16 do magazynu, 25 w nowym `DataImportProfileTests`, 4 na `ImportTargetFocus` z drugiego przeglądu; szew po I10 dodał +16; I10 +22). ⚠ Uruchamiać **dwiema partycjami** (`ConnectionExpandBindingProbe` osobno) i **zawsze z `--blame-hang --blame-hang-timeout 120s`** — zawieszenie z #94/#226/#261 wystąpiło w tej sesji i instrument NAZWAŁ podejrzanego (`CompletionRow_HighlightsMatchedPrefix`); to zawieszenie **po** zakończeniu testów, nie awaria testu |
| **Weryfikacja na żywo** | `tools/probes/DataImportProbe` (I4) — **20/20 ALL PASS** · `tools/probes/DataImportRunProbe` (I7 + **G z I8** + **H z I9** + **I z I10**) przeciwko FB5 `WI-V5.0.3.1683` — **33/33 ALL PASS**; sekcja I dokłada: detektor proponuje TAB dla wklejenia z Excela (3/3 rekordy zgodne co do 5 pól), wklejenie importuje się **bez pliku na dysku**, `.xls` → tabela istniejąca i → tabela nowa, `#N/A` z BIFF odrzucone przez kolumnę VARCHAR, data z `.xls` wraca z bazy tym samym dniem (2026-05-14), oraz **prawdziwy skoroszyt napisany przez Excela** (`Nadgodziny2.xls`: 3 arkusze, 20 pól, 1073 wiersze, ostatni numer 1074). Wcześniejsze: raport == `SELECT COUNT(*)`, Rollback cofa DELETE razem z wierszami, `Batched` zatwierdza co N i Rollback tego nie cofa, dry-run nie dotyka niczego, kolumna mieszana ląduje jako VARCHAR, `CREATE` widać z drugiego przyłączenia natychmiast (#213), katalog oddaje DOKŁADNIE te typy, o które poprosiliśmy, Rollback cofa wiersze i NIE cofa tabeli — **oraz (I9): arkusz → tabela istniejąca, arkusz → tabela nowa, prawdziwa komórka daty typuje się na `DATE` i wraca z bazy tym samym dniem (2026-04-03), a `#N/A` zostaje odrzucone przez kolumnę VARCHAR** |
| **Build** | 0 ostrzeżeń / 0 błędów (`TreatWarningsAsErrors`) · smoke: aplikacja startuje |
| **Kod w `src/`** | `EmberTern.Core/Import/**` + trzy pliki w `EmberTern.Firebird` + **pięć VM-ów i widok w `EmberTern.App`**. Rdzeń nadal ma zero Avalonia, zero `FirebirdSql`, zero UI. |
| **⭐ Kamień milowy** | **MVP (I0–I7) DOSTARCZONE: CSV/TXT → istniejąca tabela działa end-to-end**, z walidacją, raportem, decyzją transakcyjną i pamięcią ostatniej konfiguracji. **I8 dokłada drugi wariant celu — tabelę, której jeszcze nie ma.** Wszystko dalej (I9–I12) jest przyrostowe. |

### ⭐ I11 as-built — etap, który niczego nie dokładał, tylko sprawdzał rachunek (2026-07-27)

**Werdykt: §4.8 nie zostało po drodze naruszone.** §6 nazwało dowód wprost — *„jeżeli nazwane profile wymagają
zmiany choćby jednego modelu albo przebudowy sekcji UI, znaczy że §4.8 zostało naruszone"*. Nie wymagały.

**Czego NIE trzeba było ruszyć — i to jest właściwy wynik etapu:**

| | |
|---|---|
| `ImportConfiguration` | bez zmian |
| `ImportProfile` | bez zmian — nazwany profil to wiersz tej samej listy, który **ma** `Name`; `IsImplicit` znaczy dokładnie to, co znaczyło od I1 |
| `ImportPipeline`, `ImportValueConverter`, `ImportRowValidator`, `ImportMappingPlanner`, `FirebirdImportWriter` | bez zmian |
| `UserSettings.ImportProfiles`, wersja kontenera `settings.dat` | bez zmian (wersja **nie** podbita — nauka z C3) |
| Układ paska poleceń | bez przebudowy — selektor wszedł w miejsce, które §4.8.4 zarezerwowało w MVP |

**Co trzeba było dopisać — jedno miejsce, zaplanowane w I1.** `ImportProfileStore` dostał operacje na nazwanych
wpisach (`ListNamed` / `GetById` / `SaveNamed` / `NameExists` / `Rename` / `Delete` / `IsReadable`). To **nie jest**
odstępstwo: własny komentarz tej klasy od I1 mówił, że API jest celowo ograniczone do wpisu niejawnego, bo
*„named-profile operations arrive with their UI in etap I11 — adding them now would leave methods nothing calls"*
(gotcha #233). Fasada urosła o czytanie i pisanie wierszy listy, która już istniała; **kształt danych się nie
zmienił**.

⚠ **Rozbieżność do odnotowania:** prompt otwierający sesję wymieniał `ImportProfileStore` wśród rzeczy, których
dotknięcie ma oznaczać „ZATRZYMAJ I ZGŁOŚ". §4.8.4 mówi jednak o UI nazwanych profili jako o *„wyłącznie widoku
nad istniejącym magazynem"*, a sam magazyn zapowiadał te metody na I11. Rozstrzygnięte na korzyść dokumentu
projektowego: **modelem** jest `ImportConfiguration` i `ImportProfile`, a te są nietknięte. Zgłoszone jawnie,
zgodnie z obowiązkiem etapu.

**⭐⭐ Dlaczego niezgodność ze światem nie wymagała ani jednej linii kodu.** Wczytanie profilu to
`ApplyConfiguration(profile.Configuration)` i **nic więcej** — czyli ta sama droga, którą od I7 wraca „ostatnio
użyta" konfiguracja: `Recalculate` → źródło czytane na nowo → cel czytany na nowo z katalogu →
`ImportMappingPlanner` przelicza pod regułą zachowania dowodliwego → pasek gotowości. Skutek: profil wskazujący
usunięty plik daje **IMP0011**, profil wskazujący nieistniejącą tabelę daje **IMP0016**, a mapowanie na
zmienionym źródle jest **przeplanowane po nazwach**, nie odtworzone po pozycjach. W kodzie profili nie ma na to
żadnego warunku — to wypada z tego, że nie ma drugiej ścieżki wczytania. Zapinowane trzema testami.

**⭐ Profil „z przyszłości" jest POKAZANY, a nie ukryty.** §4.8.3 wymaga oznaczenia go jako nieczytelnego z
komunikatem; ukrycie wyglądałoby dla użytkownika dokładnie jak skasowanie zapisanego profilu. Predykat
`ImportProfileStore.IsReadable` jest **jeden** i obsługuje zarówno listę nazwanych, jak i przywracanie wpisu
niejawnego — dwie kopie reguły „za nowy" prędzej czy później zaczęłyby się różnić, a różnica objawiłaby się jako
profil, który lista uznaje za zdatny, a ładowanie odrzuca.

**⭐ Zakres selektora jest POWIEDZIANY NA EKRANIE.** Lista pokazuje profile tego połączenia plus te, które nie są
z żadnym związane; profil zapisany na innym połączeniu **nie jest** oferowany, bo nazywa tabelę, której ta baza
może nie mieć. Ograniczenie, którego użytkownik nie widzi, jest nieodróżnialne od profilu, który zniknął — stąd
zdanie w podpowiedzi selektora.

⚠ **Świadomie NIE zbudowane: eksport/import `.json`.** §6 wymienia go jako **opcjonalny** i nie ma go w DoD.
Wymagałby nowego serializatora w Core i dwóch nowych szwów w widoku, czyli **powiększyłby powierzchnię, o której
ten etap ma orzec, że jej nie ruszył** — a to jest jedyny produkt I11. Konsekwencja obsłużona: podpowiedź
selektora nie obiecuje wymiany plikami. Gałąź `ConnectionId == null` w `ListNamed` **zostaje**, bo to nullowalne
pole zapisanego rekordu — profil, którego żadne zapytanie nie zwraca, jest danymi nieosiągalnymi, nie brakującą
funkcją.

#### ⭐ Trzeci przegląd I11 — cztery poprawki przed scaleniem (2026-07-27)

Pełny audyt powierzchni dał więcej uwag; użytkownik wybrał z niego **cztery** i świadomie odrzucił resztę
(m.in. zmianę układu `Existing table` / `New table` — po ponownej analizie uznał, że opcje pojawiające się po
prawej stronie uzasadniają obecny układ). Żadna poprawka nie tknęła `ImportConfiguration`, pipeline'u,
konwertera, walidatora, planera ani writera.

**1. ⭐ Wiersz roboczy dostał podłogę — to zamyka U5 i jest DEFEKTEM UKŁADU, nie zmianą UX.**
Wszystkie pasy poza roboczym są `Auto`, więc gwiazdka bierze to, co zostanie — a zostać może **nic**. W
wariancie „New table" kafelek Cel jest najwyższym pasem powierzchni (siatka typów plus, na żądanie, podgląd
DDL), a pod nim dolny panel trzyma wysokość bezwzględną: Mapowanie i Podgląd po konwersji **znikały
całkowicie**. To jest dokładnie powód, dla którego chip `Mapping` „wyglądał na martwy" mimo naprawy z drugiego
przeglądu — celował poprawnie w panel o zerowej wysokości.

⚠ **`MinHeight` to tylko połowa.** Podłoga, do której nie da się dojść, nie jest podłogą: gdyby sama deklaracja
weszła bez niczego więcej, przy ciasnej powierzchni siatka wypchnęłaby na zewnątrz pas statusu. Druga połowa
jest w `ApplyBottomPanel` (§ jedyny punkt renormalizacji, #240): **dolny panel jest przycinany** do tego, co
zostaje po pasach `Auto` i po podłodze. Z dwóch pasów walczących o resztę miejsca ustępuje ten, którego
wysokość **wybraliśmy my** — zapisana wartość nigdy nie jest nadpisywana, tylko chwilowo przycięta, więc wraca
sama, gdy kafelek Cel zmaleje.

**2. Chip `Transaction` — otwiera listę.** Nawigował poprawnie od drugiego przeglądu, ale to jedyna „sekcja",
która **nie jest pasem tej powierzchni** — mieszka w pasku poleceń, kilka pikseli nad chipem. Tam
`BringIntoView` jest zawsze pusty, a obwódka focusa na `ComboBoksie` to całość informacji zwrotnej. Otwarcie
listy jest tym, czym „zabierz mnie do tej decyzji" może być dla pickera: pokazuje samą decyzję i **dalej nie
zmienia żadnego ustawienia**.

**3. ⭐ „Podstawa" wspólna dla wszystkich kolumn mówiona jest RAZ.** Po odtworzeniu profilu każdy wiersz
dostawał „from the restored configuration", więc kolumna szeroka jak kolumna nazwy powtarzała jedno zdanie tyle
razy, ile tabela ma kolumn — a linia sekcji, która powinna nieść to raz, była celowo pusta. To ta sama wada,
którą drugi przegląd usunął dla `IMP0018` (jeden fakt powiedziany dwa razy uczy, żeby nie czytać żadnego),
pomnożona przez liczbę wierszy. Reguła jest ogólna, nie dotyczy tylko odtworzenia: **jeżeli podstawa jest
identyczna dla całej siatki, jest faktem o siatce, nie o kolumnie** — schodzi na linię sekcji, a nagłówek
kolumny znika razem z komórkami (nagłówek nad pustą kolumną to właśnie „atrapa"). Gdzie dowód jest naprawdę per
kolumna — a R19 zmierzył, że kolumny mieszane są normą — zostaje przy typie, który tłumaczy.

⚠ **Reszta zostawiona świadomie:** ukryta kolumna nie odzyskuje swojej szerokości (proporcje `3*` są wspólne dla
nagłówka i wierszy, a zwinięcie ich wymagałoby konwertera `bool → GridLength` — nowego typu na zamknięcie
modułu). Po ukryciu nagłówka to czyta się jak margines, nie jak martwa kolumna.

**4. ⭐ Sufit paska gotowości nie może już schować błędu za ostrzeżeniem.** Pasek pokazuje trzy wyniki i chowa
resztę, więc jego kolejność nie jest kwestią smaku — **decyduje o tym, co znika**. Kolejność ewaluacji jest
sekcjami (środowisko → źródło → cel → mapowanie → transakcja): dobra do *czytania*, zła do *cięcia*, bo nowa
tabela **zawsze** podnosi nieblokujące `IMP0018` w sekcji Cel, czyli przed każdym wynikiem mapowania — i w
zwykłej kolejności zabierało ono jeden z trzech widocznych slotów błędom, które tłumaczą, czemu bieg jest
odmówiony.

`ImportReadinessReport.Prioritized` — blokujące najpierw, reszta potem, **stabilnie w obrębie grupy** — leży w
**Core**, nie w powierzchni, z tego samego powodu co `CanValidate`: widok cinający listę własną regułą byłby
drugą opinią o tym, co najważniejsze, i pasek z raportem zaczęłyby się w końcu różnić. Stabilność sortowania
jest tym, co utrzymuje zwinięty pasek **prawdziwym prefiksem** rozwiniętego.

#### ⭐ Drugi przegląd I11 — ergonomia powierzchni (2026-07-27)

Pięć uwag z pełnego oglądu UI, wszystkie w granicach modułu, żadna w Core i żadna w globalnych stylach.

**1 + 2. Chipy paska Ready i klikalne komunikaty — to był DEFEKT, nie kwestia gustu.** Użytkownik napisał, że
`Target`, `Mapping` i `Transaction` „sprawiają wrażenie atrap". Sprawiały, bo dwa z nich **naprawdę nic nie
robiły** w zwyczajnych stanach:

- **Target** celował w `TargetPicker` (lista istniejących tabel), a ta jest **wyłączona**, kiedy wybrany jest
  wariant „nowa tabela". `FirstFocusable` pomija kontrolki nieaktywne, więc zwracał `null` i kliknięcie znikało.
- **Mapping** celował w „wiersz wymagający uwagi", czyli `null`, gdy wszystko jest zmapowane — chip działał
  **wyłącznie wtedy, gdy coś było nie tak**.

Reguła jest teraz jedna dla wszystkich pięciu: **udostępnij sekcję, potem oddaj fokus kontrolce, o której ta
sekcja aktualnie JEST** — niezależnie od tego, czy ma problem. Zielony chip nawiguje tak samo jak czerwony.
Doszło też `BringIntoView()` **przed** `Focus()`: sam fokus niczego nie przewija, co było drugą połową wrażenia
martwoty przy długiej liście kolumn. Każda gałąź ma zejście awaryjne na kontener sekcji, więc rozstrzygnięcie
nie może już dać `null`.

⭐ **„O czym jest sekcja Cel" to decyzja ViewModelu, nie widoku** (`ImportTargetFocus`): zależy od wybranego
wariantu, a widok, który liczyłby to po swojemu, byłby drugą opinią o tym, która połowa sekcji żyje. Pusta nazwa
nowej tabeli ⇒ pole nazwy; nazwa ustalona ⇒ siatka typów, bo tam są decyzje, które zostały. Zapinowane czterema
testami — brak tej odpowiedzi był źródłem całego defektu.

Komunikaty pod chipami używają **tej samej** komendy, więc naprawa objęła je automatycznie. Każde `ReadinessItem`
ma sekcję, a każda z pięciu sekcji prowadzi teraz gdzieś konkretnie — więc wszystkie zostają klikalne; nie ma
komunikatu „bez dokąd", który trzeba by zamienić w zwykły tekst.

**3. Zdublowane ostrzeżenie — jedno zostało.** Zdanie §0.5 („tabela powstaje i jest ZATWIERDZANA przed pierwszym
wierszem") stało w dwóch miejscach jednego ekranu: jako `IMP0018` w pasku gotowości i jako banner pod siatką
typów. Usunięty został **banner**, bo pasek mówi to samo, pochodzi z Core i dodatkowo **nazywa tabelę**. Dwa
sformułowania jednego faktu uczą nie czytać żadnego. ⚠ Świadome ryzyko: pasek przycina listę do trzech pozycji,
więc `IMP0018` może trafić pod „…i N więcej" — gdyby to okazało się realne w praktyce, poprawką jest wzmocnienie
**paska**, nigdy dodanie drugiego zdania.

**4. Existing / New table — jedna siatka zamiast dwóch.** Oba warianty leżały w osobnych `Grid`ach, więc każdy
mierzył szerokość kolumny po **własnej** etykiecie i pola wejściowe zaczynały się w różnych miejscach; między
nimi stała jeszcze linia faktów, dzieląc jeden wybór na pół. Teraz to jedna siatka o dwóch wierszach i wspólnych
kolumnach — pola startują w tym samym `x`, a linia faktów zeszła **pod** parę, gdzie należy do odpowiedzi, nie do
pytania. Dokładnie układ ze szkicu użytkownika, o jeden wiersz niższy niż wcześniej.

**5. Drobiazgi.** Kolumna **Basis** jest jednoliniowa z przycięciem i pełnym tekstem w podpowiedzi: zawijanie
ustawiało wysokość całego wiersza i pozwalało objaśnieniu zdominować siatkę, którą tylko opisuje (§0.6 mówi o
**dostępności** dowodu, nie o tym, że ma być najszerszy na ekranie). Panel **Mapowanie** dostał tytuł — połowa
„Podgląd" już go miała, więc obszar roboczy czytał się jako jedna nierozdzielona powierzchnia z pływającą linią
statusu; oba tytuły mają teraz tę samą wagę (współdzielony `group-header`, użyty ponownie, nie nowy styl), a
dynamiczny licznik został przy nich jako tekst poboczny. Warstw informacji jest o jedną mniej — dzięki punktowi 3.

---

#### ⭐ Szew po przeglądzie I11 — wyjście z profilu (2026-07-27)

Przegląd wzrokowy dał dwie uwagi i obie mówiły o tym samym braku z dwóch stron: **selektor był drogą w jedną
stronę.** Dało się wejść w profil, nie dało się z niego wyjść — ani zostając przy swoich decyzjach, ani
zaczynając od zera. Zwięźle: zbudowałem wybieranie, a nie zbudowałem *nie-wybierania*.

**⭐⭐ To są dwie RÓŻNE akcje i połączenie ich byłoby błędem, nie uproszczeniem.**

| | „(no profile)" — pozycja na liście | `Reset` — przycisk |
|---|---|---|
| Profil | odłączony | odłączony |
| Decyzje na powierzchni | **zostają nietknięte** | **wyczyszczone do domyślnych** |
| Pyta? | nie — nic nie niszczy | tak, gdy jest co stracić |

Gdyby pozycja na liście czyściła powierzchnię, wybranie jej niszczyłoby pracę, o której zniszczenie nikt nie
prosił — reguła #11 wprost. Dlatego pozycja nazywa się **„(no profile)", a nie „domyślna konfiguracja"**:
druga nazwa obiecywałaby przywrócenie domyślnych, którego ta pozycja nie robi. Przywracanie domyślnych ma swój
przycisk i swoją nazwę. Wybranie pozycji mówi to zresztą na pasku komunikatów — *„decyzje na powierzchni są
niezmienione"* — bo „czy to też wyczyści moją pracę" jest jedynym pytaniem, jakie ta pozycja rodzi.

**⭐ `Reset` pyta o EMPTINESS, nie o modyfikację.** „Czy coś się zmieniło od wczytania" wymagałoby kanonicznego
porównania dwóch `ImportConfiguration`, a rekord porównuje listy po referencji — mapowanie jest nową instancją
po każdym przeliczeniu, więc taki test odpowiadałby „zmienione" zawsze i natychmiast. Pytanie zadawane jest
więc odpowiadalne: **czy na powierzchni cokolwiek stoi** (źródło, schowek, tabela docelowa, nazwa nowej tabeli
albo wybrany profil). Na pustej powierzchni `Reset` nie pyta o nic — okno dialogowe bez treści uczy tylko
odruchowego klikania „OK".

⭐ **`Reset` i „Wyczyść" przy nocie o przywróceniu to teraz TEN SAM kod** (`ResetToDefaults`). „Wyczyść" miało
własną kopię tych samych trzech linii; dwa sposoby opróżnienia powierzchni prędzej czy później opróżniałyby jej
różną ilość. Przy okazji „Wyczyść" zaczęło poprawnie odłączać profil, czego wcześniej nie robiło.

⚠ **Usunięte, bo nie miało konsumenta:** właściwość `HasProfiles`. Powstała w pierwszym podejściu i nie była
związana z niczym w XAML-u — czytały ją wyłącznie testy. To ta sama dyscyplina, dla której w tym etapie nie
powstało `MarkUsed`.

**Trzecia uwaga — separacja wizualna — przyjęta i uzasadniona asymetrią.** Pasek ma teraz dwie kreski, ale nie
są równorzędne: ta między grupą profili a grupą wykonania dostała margines **12 px** zamiast 4, bo dzieli
**dwie różne rzeczy** (zarządzanie konfiguracją vs jej uruchomienie), podczas gdy druga dzieli tylko ustawienia
*wewnątrz* grupy wykonania. Profil jest nadrzędny wobec całej konfiguracji importu, więc jego granica ma
czytać się jako mocniejsza z dwóch.

⚠ **Poprawka przy okazji, w granicach modułu:** wspólne potwierdzenie destrukcyjne (`ConfirmRequested`) niosło
tylko treść, a widok dokładał **stały** nagłówek „Opróżnij tabelę przed importem" i **stały** przycisk
„Importuj" — więc pytanie „usunąć utworzoną tabelę?" już wcześniej pojawiało się pod nazwą innej akcji. Zdarzenie
niesie teraz cały `ConfirmRequest`; profile dołożyłyby trzecie i czwarte takie pytanie.

⚠ **Świadomie NIE dodane: znacznik „zmodyfikowany" przy wybranym profilu.** Wymagałby kanonicznego porównania
dwóch `ImportConfiguration`, a rekord porównuje listy (mapowanie, tokeny logiczne) po referencji — po każdym
przeliczeniu mapowanie jest nową instancją, więc znacznik zapalałby się natychmiast po wczytaniu. Zbudowanie
takiego porównania to zmiana modelu, czyli dokładnie to, czego ten etap dowodzi, że nie było potrzebne. Selektor
mówi więc „ten profil wczytano", a nie „powierzchnia równa się temu profilowi"; `Zapisz jako…` podpowiada nazwę
wybranego profilu, więc nadpisanie go jest jednym gestem.

---

### ⭐ Szew ergonomiczny po I10 — schowek jako źródło żywe, „Odśwież", jeden łańcuch (2026-07-27)

Trzy uwagi z przeglądu I10, zgłoszone jako **domknięcie ergonomii gotowego modułu, nie nowa funkcjonalność**.
Użytkownik postawił też warunek architektoniczny: *„nie chciałbym mieć dwóch ścieżek aktualizacji"* — i jeśli
jednego punktu wejścia do łańcucha nie ma, zrobić go **teraz**, przed zamknięciem modułu.

**Odpowiedź na to pytanie brzmi: jeden punkt wejścia BYŁ i jest — `Recalculate` → `RunChainAsync`.** Cały etap
polegał na doprowadzeniu do niego trzech rzeczy, które go omijały albo nie miały jak go uruchomić. Modele,
pipeline, konwerter, walidator, mapowanie i writer — **nie tknięte** (jak w I9 i I10).

| Uwaga | Co było | Co jest |
|---|---|---|
| **1. Schowek ma być źródłem żywym** | Odczyt schowka był **komendą obok łańcucha**: pobierała tekst, przypisywała go do `Source.ClipboardText`, a łańcuch reagował dopiero na zmianę właściwości | Odczyt jest **pierwszym ogniwem łańcucha** (`ReadClipboardIfNeededAsync`). Zakładka otwarta na konfiguracji „Schowek" czyta schowek **sama**; przełączenie Plik → Schowek czyta ponownie; `Ctrl+V` i `Ctrl+R` to ręczne ponowienie |
| **2. Brakuje przycisku „Odśwież"** | Nie było żadnego sposobu uruchomienia łańcucha na życzenie — pozostawało zamknięcie i otwarcie zakładki | Przycisk **Odśwież** w pasku poleceń (ikona `Icon.RefreshCw` — ta sama, której używa drzewo metadanych, Session Manager i Table Data) + `Ctrl+R`. **Nie jest drugą ścieżką**: to `Recalculate(ImportChainTrigger.Refresh)` |
| **3. Odświeżenie musi przebudować cały stan** | Łańcuch przebudowywał wszystko poprawnie — ale **dwa FAKTY o świecie były zaryglowane**: lista tabel czytana raz na zakładkę (`_tablesLoaded`) i schowek czytany tylko przy przypisaniu | `Refresh` **zrzuca oba** i czyta je od nowa, po czym idzie ten sam łańcuch: źródło → analiza → schemat → mapowanie → gotowość → podgląd |

⭐ **Kluczowa decyzja: „powód" jest ARGUMENTEM jednego łańcucha, nie drugim łańcuchem.** `ImportChainTrigger`
ma dwie wartości — `Decision` (zmieniła się decyzja: przelicz na już ustalonych faktach) i `Refresh`
(użytkownik poprosił: zrzuć fakty i przeczytaj je ponownie). To dokładnie ta sama dyscyplina, dzięki której
„Waliduj" jest **innym writerem podanym do jednego `ImportPipeline`**, a nie drugim trybem: różnicy nie ma
gdzie się rozjechać, bo nie ma drugiej ścieżki.

⭐ **Dlaczego „nawet po Ctrl+V nie przeliczało się mapowanie" — i dlaczego to nie był błąd w mapowaniu.**
Tekst schowka docierał przez **przypisanie właściwości**, a `SetProperty` porównuje: identyczna treść to brak
`PropertyChanged`, brak `Changed`, brak łańcucha. Ponowny odczyt tej samej treści przeliczał **zero**. Odkąd
odczyt jest ogniwem, to, co dzieje się dalej, wynika z **tego, że poproszono**, a nie z tego, że wartość się
różni. Gotcha **#271**.

⚠ **Odkryte przy okazji, przez test liczący odczyty: `SourceDescriptor` nie umie powiedzieć „plik, ale jeszcze
nie wybrany".** `BuildSource` zwraca dla tego stanu `Kind == Clipboard`, więc brama oparta na rodzaju źródła
kazałaby **każdej świeżo otwartej zakładce** sięgnąć do schowka i — gdy treść wygląda tabelarycznie — przyjąć
ją jako źródło, **przy zaznaczonym „Plik"**. Brama pyta więc o to, co naprawdę niesie decyzję użytkownika
(`Source.UseFile`, czyli radio). Gotcha **#272**.

⭐ **Automatyczny odczyt przyjmuje treść tylko wtedy, gdy to TABELA — i nie wymyśla na to własnej heurystyki.**
Pierwsze pytanie idzie do `DelimiterDetector`, jedynego właściciela pytania „czy to tekst rozdzielany", więc
„tabelaryczne" znaczy tu to samo, co wszędzie indziej. Drugie pytanie istnieje tylko dlatego, że ten detektor
**świadomie odmawia** wymyślenia separatora dla jednej kolumny — a jedna kolumna wklejona z Excela to nadal
tabela. **Odświeżenie jawne nie pyta o to wcale**: „przeczytaj schowek ponownie" ma jedno uczciwe znaczenie,
więc przyjmuje to, co tam jest, **także pustkę** (powierzchnia mówi wtedy „brak źródła" przez pasek gotowości,
co jest prawdą — trzymanie poprzedniego tekstu pokazywałoby dane, których w nazwanym źródle nie ma).

⚠ **Świadoma granica, zapisana jako test, żeby nie dało jej się „naprawić" przez przypadek:** odświeżenie
**nie proponuje na nowo typów nowej tabeli**, dopóki źródło opisuje te same pola. Typy wyedytowane przez
użytkownika są decyzjami, a nadpisanie decyzji propozycją to jedyna rzecz, której reguła #11 zabrania wprost;
istniejąca reguła i tak wnioskuje ponownie, gdy zmienią się pola albo kultura — czyli wtedy, gdy grunt pod tymi
decyzjami rzeczywiście się poruszył.

**Drobne przy okazji:** `ClipboardReadRequested` przeniesione z **zdarzenia** (podłączanego po konstrukcji) do
`DataImportEnvironment.ReadClipboardAsync` — bo schowek czyta teraz **łańcuch**, więc musi być odpowiadalny już
w pierwszym przebiegu z konstruktora; wybór pliku zostaje zdarzeniem, bo jego jedynym wyzwalaczem jest komenda
użytkownika. `FileFacts` → **`SourceFacts`**: jedna linia faktów dla obu wariantów, a dla schowka niesie
**godzinę odczytu** — to jest ta postać pytania „czy to, co widzę, jest aktualne", na którą źródło żywe umie
odpowiedzieć, i to ona sprawia, że odświeżenie widocznie się potwierdza także wtedy, gdy treść jest identyczna.

### 🔴 I8 — awaria znaleziona w przeglądzie i jej dwie przyczyny (2026-07-27)

**Objaw:** nowa tabela → „Waliduj" przechodzi → „Importuj" → **aplikacja natychmiast się zamyka.**
**Diagnoza zajęła minutę, bo aplikacja loguje własne awarie**: `AppDomain.UnhandledException` zapisuje pełny
ślad stosu do `%TEMP%\EmberTern-debug.log`. Tam stało wprost: `FbException` SQLSTATE **-204 „Table unknown
XXX_GG_TMP_IMPORT_FANTOM"`, rzucone przez `FirebirdImportTargetPreparer.CountRowsAsync`, wywołane z
`ConfirmEmptyAsync`. **Zawsze czytaj ten log jako pierwszy** — nie zaczynaj od ponownego czytania kodu.

**⭐ To, że Waliduj przechodziło, a Importuj się wywalało, było samo w sobie wskazówką:** `emptyFirst` to
`!validation && …`, więc feralna ścieżka istniała wyłącznie w prawdziwym przebiegu.

| # | Defekt | Dlaczego powstał | Poprawka |
|---|---|---|---|
| **1** | **„Opróżnij tabelę przed importem" było WŁĄCZONE dla nowej tabeli.** Przebieg pytał o `COUNT(*)` z tabeli, której jeszcze nie ma — bo `CREATE` leci dopiero po potwierdzeniu | Widok **ukrywa** ten checkbox w wariancie „nowa tabela", ale `BuildBehavior` kopiowało wartość bezwarunkowo. **Ukrycie kontrolki nie wycofuje decyzji, którą ona niesie.** Wartość mogła też przyjść z **przywróconej konfiguracji „ostatnio użytej"** — wtedy użytkownik nigdy jej nie widział | `BuildBehavior` → `EmptyTargetBeforeImport = !IsNewTable && EmptyBeforeImport`. Poprawka **w jedynym miejscu tłumaczącym stan UI na rekord** (§4.8.6); strażnik u konsumenta byłby drugą opinią o tym, czyja to decyzja. Sam „ptaszek" **nie jest kasowany** — powrót do wariantu istniejącej tabeli zastaje go tam, gdzie użytkownik go zostawił |
| **2** | **Wyjątek wyszedł z komendy i zabił proces.** `AsyncRelayCommand` rzuca błąd nieudanej komendy **na dispatcherze**, gdzie nie ma już czego go złapać — więc nieobsłużony wyjątek nie daje złego raportu, tylko kończy aplikację | Klauzule `catch` były **białymi listami TYPÓW** (`InvalidOperationException or TimeoutException …`). ⭐ A ten VM sięga po świat **wyłącznie przez delegaty** z `DataImportEnvironment`, właśnie po to, by żaden typ Firebirda nie dotarł do ViewModelu (reguła #1) — i to wymazanie działa w obie strony: **komponent rozmawiający ze światem przez delegaty nie może wyliczyć wyjątków, które świat rzuca.** Moduł nie mógł nawet *nazwać* `DdlExecutionException` bez złamania własnych warstw | Nowa granica `RunGuardedAsync` + `ReportUnexpected` — łapanie **po POZYCJI (to jest granica), nie po TYPIE**. `OperationCanceledException` osobno i wyżej (anulowanie to decyzja, nie usterka — #253). Rozszerzone na wszystkie 12 styków z delegatami, na pozostałe komendy async oraz na **łańcuch przeliczeń**, który jest *fire-and-forget* i zamieniał ucieczkę w `UnobservedTaskException` |

⚠ **Defekt 2 krył jeszcze jedną, niezauważoną awarię:** `FirebirdDdlExecutor` rzuca `DdlExecutionException`,
którego nie było na żadnej liście — więc **każdy odrzucony `CREATE TABLE` również zamknąłby aplikację**,
zanim ktokolwiek zobaczyłby komunikat serwera. Znalezione przy okazji, nie przez zgłoszenie.

**Testy regresyjne (+11) i dowód, że działają:** obie poprawki zostały **tymczasowo wycofane**, a testy
`ANewTable_NeverCarriesEmptyTheTableFirst_EvenIfItWasTickedEarlier` i
`NoCollaboratorCanTakeTheApplicationDown(failing: "count")` **wywaliły się** — po czym poprawki wróciły.
Test rzuca typ, którego nikt nigdy nie wpisałby na białą listę, **z każdego współpracownika po kolei**
(create · read · count · write · commit · rollback · drop · tables), więc wywali się w dniu, w którym ktoś
znów zawęzi którykolwiek `catch`. Gotchy **#264** i **#265**.

### ✅ I8 — drugie zgłoszenie z przeglądu: nowa tabela nie pojawiała się w drzewie (2026-07-27)

**Objaw:** import przechodzi, tabela powstaje, dane są w środku — ale Explorer metadanych pokazuje ją
dopiero po ręcznym odświeżeniu. **Przyczyna, bez zagadki:** `CREATE TABLE` leci na linii Ddl i **nikt nie
zawiadamiał drzewa**; pozostałe ścieżki DDL robią to jawnie (20 wywołań `Metadata.RefreshAsync()`).

⭐ **Dlaczego nie dopisano dwudziestego pierwszego.** Użytkownik zlecił wcześniej analizę mechanizmu
metadanych ([metadata-refresh-analysis.md](metadata-refresh-analysis.md)) i pomiar pokazał, że pełne
odświeżenie kosztuje **13 zapytań do katalogu (~164 ms) plus ponad sekundę na wątku UI**, gdy jakaś
kategoria jest rozwinięta. Moduł importu **zna nazwę tabeli, którą właśnie utworzył**, więc mówi to
wprost — `DataImportEnvironment.TableCreated` / `TableDropped` — a `MetadataExplorerViewModel` wstawia
albo usuwa **jeden liść w posortowanym miejscu** (`ApplyObjectAddedInPlace` /
`ApplyObjectRemovedInPlace`, wąski precedens obok istniejącego `ApplyTriggerActiveStateInPlace`).
Zmierzone: **1,3 ms i zero okrążeń do bazy**.

⚠ **To NIE jest „Warstwa 2" z raportu** — nie ma tu wspólnego pojęcia zmiany ani protokołu, a pozostałych
20 ścieżek DDL nikt nie ruszał. Uogólnienie to osobny etap infrastrukturalny **po** zamknięciu Data Import.

**Delegat niesie NAZWĘ, nie typ metadanych** — z tego samego powodu, dla którego istnieje cały
`DataImportEnvironment` (reguła #1). I jest **stwierdzeniem faktu** („ta tabela istnieje"), nie poleceniem
(„odśwież się"): kto zna zmianę, ten ją opisuje, a nie każe drugiej stronie odkrywać ją od nowa.

**Zgłoszenie tworzenia idzie zaraz po udanym `CREATE`, nie na końcu przebiegu** — tabela przeżywa nieudany
import (§0.5, gotcha #213), więc jej istnienie nie jest warunkowane wierszami. Symetrycznie: udany `DROP`
po nieudanym imporcie zgłasza usunięcie, żeby drzewo nie oferowało obiektu, którego już nie ma.

**Przy okazji tej samej sesji weszła Warstwa 1 z raportu** (blokada `BeginUpdate/EndUpdate` na ścieżce
ładowania): projekcja pełnego odświeżenia **1 424 ms → 2 ms** przy jednej rozwiniętej kategorii. Szczegóły
i pełne pomiary — [metadata-refresh-analysis.md §7](metadata-refresh-analysis.md).

### ⭐ I10 as-built — pięć rzeczy wartych zapamiętania

1. **⭐⭐ Filar wytrzymał ostrzejszy test, bo tym razem doszła ZALEŻNOŚĆ.** I9 dokładał źródło przy pomocy
   biblioteki, którą projekt już miał; I10 dokłada `ExcelDataReader` — jedyną decyzję NuGetową, jaka w tym
   module jeszcze zapadała. Zależność sięga **dokładnie jednego projektu** (`EmberTern.Office`), a pipeline,
   konwerter, walidator, planer mapowania i writer **znów nie zostały tknięte**. `IImportProvider` ma trzecią
   implementację; App nauczył się jednego: `ProviderFor` ma o jedną gałąź więcej.
2. **⭐ Schowek był już zbudowany — etap dołożył mu wyłącznie dowody.** Przełącznik Plik/Schowek, pole
   `ClipboardText`, komenda `UseClipboardCommand`, delegat `ClipboardReadRequested` i odczyt z `TopLevel`
   w `MainWindow` powstały wcześniej (I5). Nie było czego pisać; było co **sprawdzić** — i to okazało się
   niebanalne, patrz punkt 3. ⚠ Morał na przyszłe etapy: przed napisaniem kodu z listy zakresu warto sprawdzić,
   czy nie stoi już gotowy. Prompt otwierający I10 zapowiadał „brakuje wyłącznie wczytania zawartości schowka" —
   i to akurat też już istniało.
3. **⭐⭐ Detekcja separatora jest krokiem NAD pipeline'em, i pierwsza wersja sondy tego nie odwzorowała.**
   Przypadek I1 podał `AutoDetectDelimiter = true` prosto do `ImportPipeline` i dostał **jedną kolumnę**.
   To nie jest usterka — to §0.4 działające zgodnie z projektem: auto-detekcja **proponuje**, pokazuje podstawę,
   a do konfiguracji trafia wartość **rozstrzygnięta**; provider czyta separator zadeklarowany i nie ma zdania
   o wykrywaniu. Sonda musi robić to, co robi powierzchnia, bo inaczej nie dowodzi niczego o powierzchni.
   ⚠ Uogólnienie: gdy sonda i UI dają różne wyniki na tych samych danych, **najpierw sprawdź, czy sonda
   odtworzyła całą drogę** — dopiero potem szukaj usterki w produkcie.
4. **⭐ Kalendarz Excela dostał JEDNEGO właściciela, bo drugi provider potrzebował go w drugą stronę.**
   `WorkbookCellReader` liczył serial → data u siebie (I9). ExcelDataReader oddaje `DateTime` **już zdekodowany**,
   więc uszanowanie opcji „nie traktuj komórek dat jako daty" znaczy konwersję **z powrotem** na liczbę seryjną.
   Odwrotność napisana obok funkcji prostej, której nie widzi, to dokładnie sposób, w jaki dwie połówki jednego
   kalendarza się rozjeżdżają — stąd `ExcelSerialDate` z `FromSerial` i `ToSerial`, z którego korzystają obaj
   providerzy. Zapinowane: serial 15 to 1900-01-15 w obie strony (`FromOADate(15)` dałoby 1900-01-14).
5. **⭐ Biblioteka sama decyduje „czy to data", więc jej werdykt jest PONAWIANY u nas.** To odwrotne
   niebezpieczeństwo niż w `.xlsx`: nie data przeoczona, tylko data **wymyślona**. `XlsCellReader` pyta
   `SpreadsheetNumberFormats` — jedynego właściciela tej decyzji, który kod formatu **parsuje, a nie
   przeszukuje** (gotcha #268) — więc własny format walutowy `#,##0\ [$€-1];[Red]\-#,##0\ [$€-1]` zostaje
   pieniędzmi także tutaj, choć biblioteka niezależnie uznała go za datę. Przy okazji domknęła się rozbieżność,
   której nikt by nie szukał: komórkę „sama godzina" `.xlsx` oddawał jako `TimeSpan`, a `.xls` jako
   `1899-12-31 12:00` — teraz obaj oddają `TimeSpan`.

⚠ **R8 zmierzone dla `.xls` przed napisaniem providera** (sonda `XlsFormatProbe`, usunięta po zamknięciu etapu,
zgodnie z regułą dla sond jednorazowych). Skoroszyt BIFF8 o 60 000 wierszy × 5 kolumn: sterta **płaska —
26,7 MB przy wierszu 15 000, 30 000, 45 000 i 60 000**. Kształt krzywej jest tu dowodem, a nie sama końcowa
liczba: czytnik materializujący arkusz rośnie z liczbą wierszy. Zatrzymane 19,6 MB to tablica tekstów (SST),
proporcjonalna do liczby **różnych** napisów, nie do liczby wierszy — ta sama własność, którą I9 opisał dla
`SharedStringTable`.

⚠ **Sprostowanie do I9.** I9 zapisał, że `Wynagrodzenie.xlsx` to „stary format pod nową nazwą". Pomiar I10
uściśla: to plik-kontener OLE2 (sygnatura `d0cf11e0`), ale **nie skoroszyt** — czytnik BIFF odpowiada
*„Neither stream 'Workbook' nor 'Book' was found"*. I10 **nie** daje więc możliwości odczytania tego pliku i
komunikat odmowy niczego takiego nie obiecuje.

⚠ **Zmieniony komunikat odmowy w `XlsxImportProvider`.** Do I10 radził „zapisz jako .xlsx", bo stary format był
nieczytelny w ogóle. Teraz jest czytelny, więc pierwsza rada brzmi „zmień rozszerzenie na `.xls`". Odmowa, która
po powstaniu krótszej drogi nadal poleca dłuższą, to komunikat, który po cichu przestał być prawdziwy.

⚠ **Usunięty `UiStrings.ImportFormatNotYetSupportedFormat`.** Każdy rodzaj źródła, jaki powierzchnia potrafi
rozpoznać, ma teraz providera — komunikat o stanie, który nie może już wystąpić, jest gorszy niż jego brak.

### ⭐ I9 as-built — cztery rzeczy warte zapamiętania

1. **⭐⭐ Filar się utrzymał, i to jest jedyny wynik tego etapu, który naprawdę się liczy.** I9 był
   pierwszym etapem dokładającym nowe ŹRÓDŁO, czyli pierwszym realnym testem §1.4. **Pipeline, konwerter,
   walidator, planer mapowania i writer nie zostały tknięte.** Cała wiedza o skoroszycie mieści się w
   `XlsxImportProvider`; wszystko poniżej `IImportProvider` nadal nie wie, że arkusze istnieją. Sonda H
   pokazuje to najdobitniej: sekcja przeprowadza tę samą podróż, co sekcje A–G, i jedynym nowym elementem
   jest provider. Domknęło się przy okazji ryzyko z reguły #2 — `IImportProvider` ma wreszcie **drugą**
   implementację produkcyjną (§4.3 zapowiadał ten stan jako przejściowy).
2. **⭐ Defekt w heurystyce sondy I0, znaleziony i NIE przeniesiony do produkcji.** Sonda pytała
   `code.Contains('d')`. Własny format z prawdziwego pliku użytkownika —
   `#,##0\ [$€-1];[Red]\-#,##0\ [$€-1]`, opisany w I0 jako *„waluta, NIE data"* — odpowiada na to
   **TWIERDZĄCO**, bo `[Red]` zawiera „d". Sonda tego nie wykryła, bo żaden wiersz nie używał tego stylu
   (zmierzone: „komórki będące liczbą z formatem daty: **0**"). W produkcji zamieniłoby to kolumnę z
   pieniędzmi na daty — po cichu, czyli §0.1 w najgorszej postaci. `SpreadsheetNumberFormats` **parsuje**
   kod formatu (literały w cudzysłowach, escapy, sekcje nawiasowe — `[Red]` odrzucone, `[h]` przyjęte jako
   czas), zamiast go przeszukiwać. ⭐ Morał ogólniejszy: **sonda dowodzi tego, co przypadkiem wykonała** —
   jej „PASS" nie jest dowodem poprawności heurystyki, tylko tego, że dane wejściowe jej nie ruszyły.
3. **⭐ R20 domknięte NOŚNIKIEM, nie nową regułą.** `ImportErrorKind.SourceErrorValue` istniał od I2 (z R20
   w komentarzu), `UiStrings` i mapowanie w raporcie też — brakowało wyłącznie wartości, którą provider
   mógłby powiedzieć „ta komórka jest błędem". Nowy `SourceErrorValue` (Core, **świadomie niezależny od
   formatu pliku** — `RawRecord` jest walutą wspólną dla źródeł) plus jedna gałąź w `ImportValueConverter`
   **PRZED** gałęziami typów docelowych. Kolejność jest tu istotą rzeczy: gałąź tekstowa zwraca
   `Ok(text)` bezwarunkowo, więc gdyby odmowa zależała od typu kolumny, `"#N/A"` wylądowałoby w VARCHAR jako
   dane. Sprawdzone na żywym silniku (sonda **H1b**).
4. **⭐ Epoka Excela poniżej 1900-03-01 nie jest epoką OLE.** Serial 1 to w Excelu 1900-01-01, w
   `FromOADate` 1899-12-31, a do tego Excel niesie widmowy **1900-02-29**, którego nigdy nie było. Ślepe
   `FromOADate` przesunęłoby każdą datę ze stycznia i lutego 1900 o dobę — bez słowa. Korekta jest jawna, a
   widmowy dzień **zostaje liczbą**: konwerter odmówi go dla kolumny DATE z uczciwym komunikatem, zamiast
   wymyślić datę, której nie ma w kalendarzu (§0.1).

⚠ **Domknięta luka pomiarowa z I0 (R3).** I0 uczciwie zapisał, że w pliku użytkownika **nie było ani jednej
komórki daty**, więc obsługa dat była zaprojektowana na arkuszu wygenerowanym. W tej sesji produkcyjny
provider przepuścił **osiem prawdziwych skoroszytów z dysku użytkownika**: plik `Fantomy…` odtworzył pomiary
I0 co do joty (kolumna „Nr technologii" = `double×4999 + string×1`), a `Wyceny.xlsx` **ma prawdziwe komórki
dat** — kolumna „Termin", ~450 dat na siedmiu arkuszach, roczniki 2021–2026, odczytane poprawnie. Luka
zamknięta na realnym wyjściu z Excela, nie na wygenerowanym.

⚠ **Znalezione przy okazji, warte zapamiętania:** plik o rozszerzeniu `.xlsx` **nie musi nim być**.
`Wynagrodzenie.xlsx` z dysku użytkownika to stary format pod nową nazwą i `SpreadsheetDocument.Open` odpowiada
`FileFormatException: File contains corrupted data` — czyli „twój plik jest uszkodzony", podczas gdy prawdziwa
odpowiedź brzmi „to stary format, a ta biblioteka go nie czyta" (I0 §3.5). Provider tłumaczy to na zdanie, z
którym da się coś zrobić.

### ⭐ I8 as-built — cztery rzeczy warte zapamiętania

1. **⭐⭐ `ColumnTypeInferencer` NIE MA WŁASNEGO PARSERA.** Każde pytanie „czy ta wartość mogłaby być
   liczbą / datą / logiczną" zadaje **`ImportValueConverter`** — tej samej klasie, która będzie tę wartość
   konwertowała podczas prawdziwego przebiegu, pod tą samą kulturą. To nie jest schludność, tylko jedyny
   sposób, żeby wnioskowanie i konwersja **nie mogły się rozjechać**: wnioskownik z własnym wyobrażeniem o
   tym, jak wygląda liczba, zaproponowałby typ, którego konwerter potem odmawia — czyli dokładnie bombę
   zegarową z R19. Pinuje to test `EveryValueSeen_ConvertsIntoTheTypeThatWasProposedForIt`.
2. **⭐ `ImportNewTable` jest JEDYNYM właścicielem pytania „czym staje się ta definicja kolumny".** Tekst
   typu, który waliduje podgląd, tekst typu w `CREATE TABLE` i tekst typu, który katalog odda po utworzeniu,
   to **ten sam napis** — z jednego wywołania `DdlGenerator.FormatTypeOrDomain` (§4.6: żadnego drugiego
   generatora, żadnego drugiego modelu kolumny). Gdyby powstawały w dwóch miejscach, rozjazd ujawniłby się
   jako **wiersze odrzucane przez tabelę, którą sam moduł zaprojektował**. Sonda **G4** sprawdza to na żywym
   silniku: katalog oddał `VARCHAR(2), VARCHAR(8), NUMERIC(4,2), DATE` — dokładnie to, o co poprosiliśmy.
3. **⭐⭐ Rzutowanie nowej tabeli na `ImportTarget` (`ImportNewTable.Project`) daje „Waliduj" na tabeli,
   której NIE MA — i to jest jego cała wartość.** Dry-run odpowiada na pytanie „czy te wywnioskowane typy
   naprawdę pomieszczą mój plik" **w jedynym momencie, w którym odpowiedź jest jeszcze darmowa**: po `CREATE`
   tabela jest zatwierdzona i poza zasięgiem Rollbacku (§0.5 / #213). Dzięki temu mapowanie, pasek gotowości
   i podgląd po konwersji działają dla nowej tabeli **bez ani jednej gałęzi specjalnej** — z ich punktu
   widzenia tabela, która będzie, i tabela, która jest, to to samo pytanie. Po prawdziwym `CREATE`
   koordynator **odczytuje cel z KATALOGU**, bo rzutowanie jest przewidywaniem, a katalog faktem.
4. **⭐ Wartość z wiodącym zerem NIE jest liczbą — i to jest reguła §0.1, nie reguła parsowania.** `007`
   parsuje się do 7 bez najmniejszego protestu, ale siódemka i ten tekst to **różne dane**: kod pocztowy,
   indeks, numer konta wracają z bazy inne, niż do niej weszły. To wprost reguła #11 („nigdy nie zmieniaj
   tego, czego nie umiesz odtworzyć identycznie"), więc taka kolumna ląduje jako `VARCHAR`. Pojedyncze zero
   (`0`, `0,5`) zostaje liczbą.

⚠ **Trzy granice przyjęte świadomie.** (a) **Nic nie jest wnioskowane jako `NOT NULL`** — brak dziur w pliku,
który mamy, nie mówi nic o następnym, a ograniczenie przeżywa import; zostaje decyzją użytkownika w siatce.
(b) **`SMALLINT` nie jest nigdy proponowany** — jest trafny dla pliku w ręku i niedobry dla następnego, a
różnica względem `INTEGER` nie jest warta odrzuconego wiersza. (c) **`DOUBLE PRECISION` nie jest kandydatem**
— jedyny tekst, który przyjąłby, a `NUMERIC` nie, to notacja wykładnicza, której konwerter i tak nie
przyjmuje; wybór typu przybliżonego dla wartości wyglądających na dokładne gubiłby cyfry po cichu (§0.1).

**Ponowne wnioskowanie** biegnie według reguły zachowania dowodliwego (§4.7): siatka jest przeliczana, gdy
zmienią się **pola źródła** albo **kultura** (jedno i drugie realnie zmienia, jakie typy są poprawne), a poza
tym zostaje nietknięta. Kolumny z **przywróconej konfiguracji są adoptowane takie, jakie są** — nadpisanie ich
propozycją w chwili otwarcia zakładki byłoby defektem „starszy build po cichu okradł profil" w nowym
przebraniu (§4.8.6). Skan jest najdroższym ogniwem łańcucha i jedzie na tym samym anulowalnym tokenie, co
reszta: nowsza edycja porzuca trwający skan, zamiast się z nim ścigać.

### ⭐ I7 as-built — trzy rzeczy warte zapamiętania

1. **PODGLĄD PO KONWERSJI TO PRAWDZIWY IMPORT, nie jego imitacja.** §3.6 obiecuje, że siatka pokazuje
   „dokładnie to, co trafi do bazy" — i ta obietnica jest prawdziwa wyłącznie dlatego, że wypełnia ją
   `ImportPipeline`: ten sam konwerter, walidator, mapowanie i kultura. Powstały do tego **dwa dodatki w Core,
   oba addytywne**: `BoundedImportProvider` (dekorator ograniczający ODCZYT — bo podgląd nie może czytać
   miliona wierszy, żeby pokazać sto) i `PreviewImportWriter` (writer, który wiersze **zatrzymuje** zamiast
   wysyłać). To ta sama dyscyplina, co „Waliduj to inny argument, nie inny tryb", tylko piętro wyżej:
   **inny provider i inny writer, ten sam jeden import.** Prywatna procedura „przekonwertuj na potrzeby
   wyświetlenia" byłaby drugą ścieżką, a druga ścieżka się rozjeżdża — i nikt by tego nie zauważył.
   ⚠ **Wiersz z błędem pokazuje wartości SUROWE i to nie jest półśrodek:** pipeline zatrzymuje wiersz na
   pierwszej złej wartości, więc taki wiersz **nie ma** wartości przekonwertowanych — a surowe są dokładnie
   tym, co użytkownik ma poprawić.
2. **`CanValidate` jest słabsze od `CanRun` — i to decyzja, nie niedopatrzenie.** Otwarta transakcja robocza
   blokuje **import**, ale nie **walidację**: dry-run nie pisze nigdzie, więc zablokowanie go odmawiałoby
   jedynej operacji, która pomaga *właśnie wtedy*, gdy użytkownik zastanawia się, co zrobić z transakcją.
   Reguła mieszka w Core (`ImportReadinessReport.CanValidate`) — „co ten raport dopuszcza" to pytanie tego
   rekordu; rozstrzyganie go w widoku byłoby drugą opinią o gotowości.
3. **`Batched`: `CommitEveryRows` jest PODŁOGĄ, nie dokładną wielokrotnością — zmierzone na żywo.** Commit
   może paść wyłącznie na granicy paczki (`BatchedCommitImportWriter` jest dekoratorem, a nie zmianą w
   `FirebirdImportWriter`, żeby `Manual` i `AutoCommitOnSuccess` biegły bajt w bajt tym samym kodem), więc
   ląduje na **pierwszej granicy paczki na N lub za N**. Przy zmierzonych wartościach domyślnych (paczka 500,
   commit 10 000) trafia dokładnie w 10 000; interwał commitu **mniejszy** od paczki daje jeden commit na
   paczkę. Alternatywą byłoby skrócenie paczki — a I0 zmierzył, że to właśnie rozmiar paczki kosztuje
   przepustowość, podczas gdy częstotliwość commitu jest niemal darmowa. Wygrywa paczka, ugina się commit,
   i jest to powiedziane wprost, bo to liczba, względem której czytany będzie raport.

Dodatkowo: `DataImportEnvironment` zastąpił pięć pozycyjnych delegatów **jednym nazwanym pakietem** —
I7 dokłada sześciu współpracowników, a jedenaście pozycyjnych argumentów w miejscu wywołania to miejsce, w
którym dwa da się zamienić i nic nie zaprotestuje. Nowe w `EmberTern.Firebird`:
`FirebirdImportTargetPreparer` (`COUNT(*)` + `DELETE FROM` na linii **Data**, w transakcji użytkownika — bo
opróżnienie tabeli to dane, nie schemat) i wspomniany `BatchedCommitImportWriter`.

### Zakres pozostały modułu (stan po I7)

| Etap | Co zostało | Blokady / zależności |
|---|---|---|
| ~~**I6**~~ ✅ | sekcja **Cel** (istniejąca tabela) + panel **Mapowanie** + przeliczanie łańcuchowe z anulowaniem | dostarczone; układ rozstrzygnięty przed implementacją, więc I6 wstawił się w gotową ramę |
| ~~**I7**~~ ✅ | Dostarczone — patrz „I7 as-built" wyżej | pas **B** powstał tutaj, razem z zakładkami Błędy/Raport; **KONIEC MVP** |
| ~~**I8**~~ ✅ | Dostarczone — patrz „I8 as-built" wyżej. Ponadto: **`IMP0028 NewTableAlreadyExists`** (zajęta nazwa blokuje **przed** przebiegiem, bo `CREATE` jest pierwszą rzeczą, którą import robi — inaczej zielony pasek i natychmiast surowy błąd serwera) | wstawiło się w kafelek Cel z I6 bez przebudowy |
| ~~**I9**~~ ✅ | Dostarczone — patrz „I9 as-built" wyżej. Ponadto: **`SourceErrorValue`** (nośnik domykający R20) i **`ListSheetsAsync`** na porcie, dzięki czemu powierzchnia pyta o arkusze *dostawcę*, a nie zna typu skoroszytu | `DataImportXlsxProbe` **usunięta** — jej rolę przejął kod produkcyjny + sekcja H w `DataImportRunProbe` |
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
    ── I8 ──────────────────────────────────────────────────────────────────────────────────────
    ColumnTypeInferencer.cs  ⭐ propozycja typów ze SKANU CAŁEGO ŹRÓDŁA (REK-7 / R19; limit 1 M).
                             ⭐⭐ NIE MA własnego parsera — każde „czy to mogłaby być liczba"
                             zadaje `ImportValueConverter`, tej samej klasie, która będzie tę
                             wartość konwertowała. Wnioskowanie i konwersja nie mogą się rozjechać,
                             bo jest tylko jedno. §0.3: kandydat musi pasować do KAŻDEJ wartości;
                             pierwsza, do której nie pasuje, go zabija — a dowód (wartość + numer
                             wiersza) zostaje w `ColumnInferenceEvidence`. Wiodące zero to NIE
                             liczba (reguła #11: `007` ≠ 7). Nic nie jest wnioskowane `NOT NULL`
    ImportNewTable.cs        ⭐ JEDYNY właściciel „czym staje się ta definicja kolumny": tekst typu
                             dla podglądu, `CREATE`/`DROP` przez współdzielony `DdlGenerator`
                             (§4.6 — zero drugiego generatora) i `Project()` → `ImportTarget`,
                             dzięki któremu „Waliduj", mapowanie i podgląd działają na tabeli,
                             KTÓREJ JESZCZE NIE MA — w jedynym momencie, w którym odpowiedź jest
                             jeszcze darmowa (§0.5)
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

### Testy I8 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `ColumnTypeInferencerTests` (36) | ⭐⭐ **`EveryValueSeen_ConvertsIntoTheTypeThatWasProposedForIt`** — każda wartość, którą wnioskownik zobaczył, musi przejść przez konwerter na typie, który dla niej zaproponował; gdyby to kiedyś padło, moduł zaprojektowałby tabelę, której własny import nie umie wypełnić (R19 odtworzone) · ⭐ **kolumna mieszana → `VARCHAR` z nazwaniem wartości i WIERSZA, który przesądził** · ⭐ **wiodące zero to nie liczba**, ale pojedyncze zero i `0,5` już tak · skan obejmuje CAŁE źródło (dyskwalifikująca wartość leży za 3. wierszem — próbka by ją przegapiła) · limit bezpieczeństwa mówi, że zadziałał · anulowalność · `INTEGER`→`BIGINT` tylko gdy trzeba, nigdy `SMALLINT` · precyzja i skala z tego, co widziano · liczba szersza niż 18 cyfr → tekst · `0`/`1` czytane jako `INTEGER`, nie jako `BOOLEAN` · długość `VARCHAR` = najdłuższa napotkana wartość · bardzo długi tekst → `BLOB SUB_TYPE 1` · **nic nie jest `NOT NULL`** · nazwy kolumn przez `ImportMappingPlanner.NormalizeName` (więc planer paruje je z powrotem bez gałęzi specjalnej) + rozstrzyganie duplikatów |
| `ImportNewTableTests` (13) | ⭐⭐ **każdy typ, który ten moduł emituje, `ImportTargetType` czyta z powrotem** jako typ, który import umie zapisać — rzutowanie i katalog opisują tę samą kolumnę · ⚠ jedno `Size` z `ImportColumnDefinition` trafia do WŁAŚCIWEJ szuflady `FieldDefinition` (`Size` dla tekstu, `Precision` dla `NUMERIC`) · rozmiar na typie, który go nie bierze, nie wycieka do DDL · `CREATE` z współdzielonego generatora, z cytowaniem nazw · **żadnych wymyślonych ograniczeń** (PK / identity / DEFAULT) · wiersz bez nazwy to nie kolumna · rzutowanie: każda kolumna zapisywalna, zero triggerów |
| `DataImportNewTableTests` (17) | ⭐⭐ **`TheTableIsCreatedBeforeTheFirstRow`** — gotcha #213 jako asercja KOLEJNOŚCI (`create` przed `begin`), bo każde zdanie powierzchni o Rollbacku wynika właśnie z niej · ⭐⭐ **`Validate_AnswersTheQuestion_WithoutCreatingAnything`** — dry-run na rzutowaniu, więc pytanie „czy te typy pomieszczą mój plik" ma odpowiedź, zanim cokolwiek powstanie · ⭐ **writer dostaje cel z KATALOGU, nie rzutowanie** (fikcyjny katalog celowo oddaje `VARCHAR(999)` tam, gdzie rzutowanie mówi `VARCHAR(2)`, więc test nie może przejść przypadkiem) · zajęta nazwa blokuje PRZED przebiegiem i żaden DDL nie leci · nieudany `CREATE` zatrzymuje przebieg, zanim poleci pierwszy wiersz · ⚠ **sprzątanie to dwa skutki i jedno pytanie**: najpierw Rollback, potem `DROP`, a pytanie mówi o obu · odmowa zostawia tabelę · udany import nigdy nie sprząta · domyślnie wyłączone · edycja typu dociera do rekordu i do DDL · **kolumny z przywróconego profilu nie są nadpisywane świeżą propozycją** |

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
| ~~`tools/probes/DataImportXlsxProbe`~~ | sonda odczytu `.xlsx` | **usunięta w I9** — zastąpiona kodem produkcyjnym, `XlsxImportProviderTests` i sekcją H `DataImportRunProbe` |
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
| XLS (BIFF8) | `XlsImportProvider` | `EmberTern.Office` | Dostarczone w I10 (D2). Czytanie strumieniowe przez `ExcelDataReader` — jedyna zależność NuGet dołożona poza MVP. |

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
- **As-built I8:** dla kolumny mieszanej „Podstawa" nazywa **wartość i numer wiersza**, który przesądził
  o zejściu do `VARCHAR` (np. *„mieszane — liczby całkowite do wiersza 8724 „11 88x"; tekst, najdłuższa 18"*)
  — bo R19 zmierzył, że mieszane kolumny są normą, a odesłanie użytkownika do konkretnej linii jego pliku
  jest jedyną formą, w której to wyjaśnienie da się sprawdzić (§0.6). Wiersz przywrócony z profilu **nie
  pożycza cudzej podstawy** i mówi wprost, że pochodzi z konfiguracji.
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
> **U5 — ZAMKNIĘTY 2026-07-27** (trzeci przegląd I11). Weryfikacja, na którą czekał, wypadła negatywnie i to
> dobrze, że czekał: gdy sekcja Cel faktycznie zajęła miejsce — w wariancie „New table", z siatką typów i
> podglądem DDL — **powierzchnia robocza schodziła do zera**, bo wiersz `*` nie miał podłogi. Domknięte
> `MinHeight` na wierszu roboczym **plus** zacisk dolnego panelu w `ApplyBottomPanel`, bez którego podłoga
> byłaby nieosiągalna (szczegóły w bloku „⭐ Trzeci przegląd I11").
>
> **Otwarty zostaje jeden punkt, świadomie poza modułem: U4** (gęstość globalna → sprint UX całego
> EmberTerna po zamknięciu modułu).

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
| `DELETE FROM` (opróżnij przed importem, D5) | **własna sesja modułu** | ta sama transakcja co wiersze | To dane, nie schemat — musi być wycofywalne razem z importem. |
| `INSERT` wierszy | **własna sesja modułu** | **WŁASNA transakcja Data Import** (auto-begin, nigdy auto-commit) | ⭐ **ZMIENIONE w I7.5 — patrz amendment niżej.** Reguła #3 nadal obowiązuje; zmienia się tylko to, CZYJA jest ta transakcja. |

> ### ⭐ AMENDMENT I7.5 (2026-07-26) — Data Import ma WŁASNĄ transakcję roboczą
>
> **Ratyfikowane przez użytkownika po przeglądzie I7 i po pomiarach.** Pierwotnie moduł pisał do JEDNEJ
> transakcji roboczej użytkownika — tej samej, z której korzysta SQL Editor. Rozumowanie („import to operacja
> użytkownika jak F5") było spójne dla konsoli, ale miało konsekwencję, której nikt nie zamierzył:
> **przycisk `Commit` w imporcie zatwierdzał także pracę zostawioną niezatwierdzoną w SQL Editorze.**
> Przycisk musi robić dokładnie to, co komunikuje (reguła #11 / §0.5), więc możliwość została **usunięta**,
> a nie opatrzona ostrzeżeniem.
>
> **Zmierzone przed decyzją** (`tools/probes/ImportTransactionIndependenceProbe`, FB5, 8/8 PASS): sterownik
> odmawia drugiej transakcji na jednym `FbConnection`, ale **dwa przyłączenia dają dwie w pełni niezależne
> transakcje** — zatwierdzane i wycofywane osobno, wzajemnie niewidoczne przed commitem. „Druga niezależna
> transakcja" znaczy więc „drugie przyłączenie", dokładnie jak w Debuggerze.
>
> **Cena, przyjęta świadomie:** `SELECT` w SQL Editorze **nie zobaczy** zaimportowanych wierszy przed
> commitem, a kolizja na tym samym wierszu kończy się natychmiastowym błędem (zmierzone: SQLSTATE 40001
> w ~28 ms pod NOWAIT) zamiast cichego współdzielenia.
>
> **Skutki uboczne:** `ImportReadiness` przestał w ogóle raportować transakcję konsoli (kod `IMP0021`
> wycofany, numer nieużywany ponownie) — co rozwiązało sprzeczność, którą projekt niósł od I2: §3.2 nazywał
> otwartą transakcję roboczą **blokującą**, podczas gdy §4.5 kazał writerowi do niej **dołączać**. Oba nie
> mogły być prawdą naraz. Gwardie zamknięcia i rozłączenia pytają teraz **jednego właściciela**
> (`PendingWorkRegistry`), a nie wymieniają modułów po nazwie.
>
> Dowód na żywo: `tools/probes/DataImportRunProbe` przypadek **F** — konsola zostawia wiersz niezatwierdzony,
> import commituje, konsola wycofuje, zostaje **tylko import**.

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
| **I7** ✅ **DOSTARCZONY** | App: Podgląd + uruchomienie + raport — **pierwszy pełny przebieg** | Podgląd po konwersji (ciągły), `Waliduj`, tryby transakcji, `Importuj`/`F5`, postęp, anulowanie, raport, Commit/Rollback, eksport raportu, **zapis i przywracanie „ostatnio użytej" konfiguracji**. | **Import CSV → istniejąca tabela działa end-to-end na żywej bazie; druga sesja startuje z przywróconą konfiguracją.** Pierwszy etap z realną wartością dla użytkownika. **Spełnione** — +24 testy (5607 zielonych) oraz `tools/probes/DataImportRunProbe` **11/11 ALL PASS** na żywym FB5 (raport == `SELECT COUNT(*)`). |
| **I8** ✅ **DOSTARCZONY** | Nowa tabela | `ColumnTypeInferencer` — **(I0/REK-7) domyślnie skanuje CAŁE źródło**, nie próbkę (limit bezpieczeństwa 1 M wierszy), bo w realnym pliku 2 z 5 kolumn były typowo mieszane (R19); siatka typów w sekcji Cel z **zawsze widoczną liczbą przeanalizowanych wierszy** w kolumnie „Podstawa"; podgląd DDL; wykonanie na linii Ddl; ostrzeżenie o nieodwracalności; opcja `DROP` przy niepowodzeniu. | Import do nieistniejącej tabeli działa; typy zachowawcze i edytowalne; DDL z tego samego generatora; **kolumna mieszana ląduje jako `VARCHAR`, nie jako `INTEGER` z bombą zegarową**. **Spełnione** — +86 testów (5693 zielone) oraz sekcja **G** w `DataImportRunProbe`: **20/20 ALL PASS** na żywym FB5, w tym dowód, że katalog oddaje dokładnie te typy, o które poprosiliśmy, i że Rollback cofa wiersze, a nie tabelę. |
| **I9** | XLSX + zmiana nazwy projektu (D1) | `EmberTern.Export.Office` → **`EmberTern.Office`**; `XlsxImportProvider`; rozgałęzienie sekcji Format po `Capabilities`. **(I0/REK-6) Siedem wiążących wytycznych providera:** (1) **wyłącznie `OpenXmlReader` (SAX)** — DOM bierze 77× więcej pamięci (R8); (2) wartości umieszczane **po `CellReference`** — brakująca komórka środkowa jest NIEOBECNA, nie pusta, więc czytnik pozycyjny przesunąłby resztę wiersza o kolumnę (§0.1); (3) numer wiersza źródłowego **z `Row.RowIndex`** — puste wiersze są nieobecne, własny licznik skłamałby w raporcie (§0.6); (4) data = liczba + `numFmtId` daty (R3); (5) `SharedStringTable` czytana raz — Excel zapisuje teksty jako shared strings (rozmiar ∝ liczbie RÓŻNYCH tekstów); (6) `SheetDimension` **tylko jako wskazówka** postępu (bywa nieobecny); (7) formuła → wartość zbuforowana, a **komórka błędu → błąd wiersza** (+ opcja `ExcelErrorCellsAsNull`, R20). | Import plików z załączonych zrzutów daje identyczne dane. Eksport XLSX bez regresji. Pierwszy realny plik **z datami** obejrzany (luka pomiarowa R3). Po zamknięciu I9 `tools/probes/DataImportXlsxProbe` idzie do usunięcia. |
| **I10** ✅ **DOSTARCZONY** | Schowek + XLS | Schowek (App czyta, Core parsuje — zero nowego parsera; **okazał się już zbudowany w I5**, etap dołożył dowody). `XlsImportProvider` (BIFF8) + zależność **`ExcelDataReader` 3.7.0, MIT** w `EmberTern.Office` (D2/R5) + `XlsCellReader` + wspólny `ExcelSerialDate`. Usunięta odmowa dla `.xls` i martwy już `ImportFormatNotYetSupportedFormat`. | Wklejenie z Excela importuje się bez zapisywania pliku. **Spełnione** — +22 testy (5785 zielonych) oraz sekcja **I** w `DataImportRunProbe`: **33/33 ALL PASS** na żywym FB5, w tym prawdziwy skoroszyt napisany przez Excela. R8 zmierzone przed implementacją: sterta **płaska 26,7 MB** przez 60 000 wierszy BIFF8. |
| **I11** ✅ **DOSTARCZONY** | Nazwane profile (UI) | Selektor profili w zarezerwowanym miejscu paska poleceń, `Zapisz jako…`, zmiana nazwy, usuwanie, opcjonalny eksport `.json`. **Zero zmian w modelach i w pipeline.** | Nazwany profil odtwarza cały import; niezgodności raportowane przez pasek gotowości (§4.8.5). **Spełnione — i to jest wynik etapu: rachunek §4.8 się zgodził.** Ani `ImportConfiguration`, ani `ImportProfile`, ani pipeline, konwerter, walidator, planer mapowania i writer nie zostały zmienione; `ImportProfileStore` urósł o operacje na nazwanych wpisach, które jego własny komentarz z I1 zapowiadał na ten etap. +34 testy (5835 zielonych), w tym trzy dowody §4.8.5: usunięty plik → IMP0011, brak tabeli → IMP0016, zmienione pola źródła → mapowanie **przeplanowane po nazwach**, nie odtworzone po pozycjach. **Eksport `.json` świadomie pominięty** (opcjonalny, poza DoD — powiększyłby powierzchnię, o której etap ma orzec, że jej nie ruszył). |
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
| R5 | ✅ **ZAMKNIĘTE (I10).** `.xls` (BIFF8) wymagało nowej zależności — i wymagało jej naprawdę: I0 zmierzył, że `DocumentFormat.OpenXml` nie otwiera takiego pliku w ogóle. | — | — | **D2 zrealizowane**: `ExcelDataReader` 3.7.0 (MIT, strumieniowy) w `EmberTern.Office` — jedynym projekcie, w którym zależność od formatu Office jest dozwolona. Do I10 obowiązywała odmowa z powodem, zgodna z §0; teraz format jest czytany naprawdę, a odmowa została zawężona do pliku, który **nie jest** tym, czym się przedstawia. |
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
