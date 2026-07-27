# Prompt otwierający sesję implementacyjną — etap I9 (Data Import)

Plik jest jednorazowym **materiałem sesyjnym**, nie dokumentem architektonicznym. Treść w bloku niżej
wkleja się jako pierwsza wiadomość nowej sesji. Po zamknięciu I9 plik zastępuje się analogicznym dla I10.

I9 jest **pierwszym etapem, który dokłada nowe ŹRÓDŁO**. Do tej pory moduł czytał wyłącznie tekst
rozdzielany (CSV / TXT / schowek) jednym providerem; I9 dokłada drugi — arkusz `.xlsx` — i tym samym po raz
pierwszy sprawdza, czy filar „jeden pipeline dla każdego źródła" faktycznie się trzyma. Jeżeli dodanie
providera wymaga zmiany czegokolwiek poniżej `IImportProvider`, coś po drodze poszło nie tak.

---

```
EmberTern — sesja implementacyjna: Data Import, etap I9 (XLSX). MVP + I8 są zamknięte.

Zacznij od przeczytania CLAUDE.md i docs/design/data-import.md — blok „📍 STAN IMPLEMENTACJI",
a potem sekcje §0 (prawo nadrzędne modułu), §1.5 (odwzorowanie źródeł na providerów),
§3.3 (sekcja Źródło i format + rozgałęzienie po Capabilities), §4.3 (porty), §4.4 (pipeline)
i wiersz I9 w §6. Przeczytaj też „⭐ I8 as-built" — cztery rzeczy, których nie wolno zepsuć.

⭐ ORAZ, OBOWIĄZKOWO: docs/design/data-import-i0-findings.md, sekcje §3 (pomiary .xlsx)
i REK-6. I9 jest jedynym etapem, którego zakres jest w całości podyktowany pomiarem —
siedem wytycznych providera to nie sugestie, tylko wnioski z liczb.

Architektura modułu jest ZAMROŻONA. I9 to wyłącznie implementacja. Odkrycie, które naprawdę
podważa projekt, oznacza: ZATRZYMAJ ETAP I ZGŁOŚ — nigdy cichy redesign.

═══ STAN WEJŚCIOWY ═══
  gałąź feat/data-import, etapy I0–I8 zamknięte, 5693 testy zielone, build 0/0,
  aplikacja startuje czysto.

  ⭐ CAŁA DROGA „ŹRÓDŁO → TABELA (istniejąca LUB nowa)" JEST GOTOWA I ZWERYFIKOWANA NA ŻYWYM FB5:
    tools/probes/DataImportProbe     (I4) — 20/20 ALL PASS
    tools/probes/DataImportRunProbe  (I7 + sekcja G z I8) — 20/20 ALL PASS
    tools/probes/DataImportXlsxProbe (I0) — sonda ODCZYTU .xlsx; to jej pomiary są podstawą REK-6

  Do UŻYCIA, nie do przepisania — po dołożeniu providera NIC z tego nie powinno się zmienić:
    ImportPipeline / ImportValueConverter / ImportRowValidator / ImportMappingPlanner
    ImportReadiness / ImportTargetType / ColumnTypeInferencer / ImportNewTable
    FirebirdImportWriter / DryRunImportWriter / PreviewImportWriter / BoundedImportProvider
    DataImportTabViewModel (JEDYNY właściciel ImportConfiguration) + DataImportEnvironment

  ⭐ TO JEST TEST PROJEKTU, nie tylko dostawa funkcji. §1.4 mówi „jeden pipeline dla każdego źródła",
  a do dziś istniał jeden provider, więc twierdzenie nie było niczym sprawdzone. Jeżeli dołożenie
  XlsxImportProvider wymaga zmiany pipeline'u, konwertera, walidatora, mapowania albo writera —
  ZATRZYMAJ SIĘ I ZGŁOŚ. Providerowi wolno dodać własne opcje (SpreadsheetOptions istnieje od I1)
  i własne Capabilities; nic poniżej IImportProvider nie ma prawa go zauważyć.

═══ ZAKRES I9 (z §6) ═══
  1. Zmiana nazwy projektu (decyzja D1): EmberTern.Export.Office → EmberTern.Office.
     Projekt przestaje być „tylko eksportowy", bo import będzie z niego czytał. Zmiana nazwy,
     nie zmiana architektury — jedyna dozwolona zależność NuGet na Office zostaje tam, gdzie była.
  2. XlsxImportProvider — SIEDEM WIĄŻĄCYCH WYTYCZNYCH z REK-6 (niżej).
  3. Rozgałęzienie sekcji Format po `Capabilities`, a NIE po ImportSourceKind w widoku.
     Provider deklaruje, co ma sens; UI za tym idzie. To już jest w kontrakcie
     (ImportProviderCapabilities.Spreadsheet: arkusze + zakres wierszy, bez separatorów i kodowania) —
     I9 ma z tego skorzystać, a nie dopisać `if (kind == Xlsx)` w XAML-u.
  4. Usunięcie z TryCreateSource odmowy „format nie jest jeszcze obsługiwany" dla .xlsx
     (dla .xls odmowa ZOSTAJE — to etap I10, a udawanie obsługi łamie §0).

═══ ⭐ SIEDEM WYTYCZNYCH PROVIDERA (REK-6) — ZMIERZONE, NIE WYMYŚLONE ═══
  1. ⭐ WYŁĄCZNIE OpenXmlReader (SAX). Nigdy DOM. Zmierzone: 100 000 wierszy to 3,9 MB sterty
     przy SAX i 300,5 MB przy DOM — 77×, czyli ~3 GB na milionie wierszy (R8).
  2. ⭐ Wartości umieszczane po CellReference, NIGDY pozycyjnie. Brakująca komórka w środku wiersza
     jest NIEOBECNA, nie pusta — czytnik pozycyjny przesunąłby całą resztę wiersza o kolumnę,
     czyli po cichu wpisałby dane do złych kolumn (§0.1, najgorsza klasa błędu w tym projekcie).
  3. ⭐ Numer wiersza źródłowego z Row.RowIndex, nigdy z własnego licznika. Puste wiersze są
     w arkuszu NIEOBECNE, więc licznik skłamałby w raporcie (§0.6) — a cały I3 był o tym,
     żeby raport nazywał wiersz, który użytkownik znajdzie w swoim pliku.
  4. Data = liczba + numFmtId będący formatem daty (wbudowane 14–22 / 45–47, albo własny kod
     zawierający y/d/h/s). Przy niejednoznaczności wartość idzie jako LICZBA i podgląd to pokazuje.
  5. SharedStringTable czytana raz — Excel zapisuje teksty jako shared strings.
  6. SheetDimension TYLKO jako wskazówka postępu — bywa nieobecny (zmierzone: jest w plikach
     z Excela, nie ma w generowanych programowo).
  7. Formuła → wartość zbuforowana. ⭐ Komórka błędu (#N/A, #REF!) to NIE tekst — musi być
     BŁĘDEM WIERSZA, nigdy ciągiem „#N/A" udającym dane (R20). Opcja
     ImportBehaviorOptions.ExcelErrorCellsAsNull JUŻ ISTNIEJE (domyślnie false) — użyj jej,
     nie dodawaj drugiej.

═══ WIĄŻĄCE DECYZJE — NIE PODEJMOWAĆ ICH PONOWNIE ═══
  • Provider zwraca RawRecord z wartościami NATYWNYMI (DateTime, double, bool), nie tekstem.
    ImportValueConverter ma już gałąź ConvertNative i jest jej jedynym właścicielem — provider
    NIE konwertuje niczego poza odczytem komórki.
  • ⭐ ColumnTypeInferencer (I8) też karmi się przez IImportProvider, więc wnioskowanie typów dla
    NOWEJ tabeli z arkusza dostaniesz za darmo — pod warunkiem, że provider zwraca wartości
    natywne. Jeśli musiałbyś dotknąć wnioskownika, znaczy, że provider oddaje tekst.
  • .xls (BIFF8) to etap I10, nie I9. Przy wyborze .xls komunikat „format nie jest jeszcze
    obsługiwany — zapisz jako .xlsx". Odmowa z powodem jest zgodna z §0; udawanie obsługi nie.
  • Reguła #2 (żadnych interfejsów bez dwóch implementacji) zostaje SPEŁNIONA dopiero tutaj:
    IImportProvider ma dziś jedną implementację, przejściowo, zgodnie z §4.3. XlsxImportProvider
    jest tą drugą i zamyka dług.
  • Reguła #6: kody, nigdy teksty. Reguła #1: zero typów Avalonia w VM.
  • Jedna powierzchnia komunikatów: MessageBanner.

═══ ⛔ ZAKAZ ZMIAN UI POZA MODUŁEM ═══
  Nie inicjuj żadnych globalnych zmian UI ani refaktoryzacji styli. Przebudowa kontrolek Avalonia,
  zagęszczenie interfejsu, responsywność i style to OSOBNY SPRINT UX, świadomie zaplanowany PO
  zakończeniu całego modułu Data Import (decyzja użytkownika, CLAUDE.md). Nie dotykaj
  Themes/ControlStyles.axaml. Drobna uwaga dotycząca JEDNEGO ekranu może być poprawiona przy
  okazji; cokolwiek dotykającego całej aplikacji — nie. Najpierw dowozimy moduł.

═══ DEFINITION OF DONE ═══
  build 0/0 · pełny zestaw testów zielony (baza 5693) · aplikacja startuje czysto ·
  ⭐ IMPORT Z PLIKU .XLSX DZIAŁA END-TO-END NA ŻYWEJ BAZIE LABORATORYJNEJ — zarówno do tabeli
     istniejącej, jak i do NOWEJ (I8 musi działać dla arkusza tak samo, jak działa dla CSV) ·
  ⭐ PIERWSZY REALNY PLIK Z DATAMI OBEJRZANY — I0 zapisał to jako jawną LUKĘ POMIAROWĄ: w pliku
     użytkownika nie było ANI JEDNEJ komórki daty, więc obsługa dat jest zaprojektowana na arkuszu
     wygenerowanym, nie na wyjściu z Excela. Bez tego punktu DoD nie jest spełniony ·
  eksport XLSX bez regresji (ten sam projekt zmienia nazwę, więc trzeba to sprawdzić) ·
  po zamknięciu I9: usunąć tools/probes/DataImportXlsxProbe (jej rolę przejmuje kod produkcyjny) ·
  commit na feat/data-import · push na origin ORAZ private ·
  aktualizacja bloku „📍 STAN IMPLEMENTACJI" + wiersza I9 w §6 w data-import.md ·
  przygotowanie promptu otwierającego I10.

  ⚠ Reguła QA projektu: build + testy + smoke NIE wystarczą, żeby nazwać etap UI zrobionym.
  I9 kończy się zgłoszeniem „implementacja gotowa — oczekuje wizualnego potwierdzenia",
  a etap zamyka dopiero mój przegląd w OBU paletach.

  ⚠ ZALEGŁOŚĆ Z I8: potwierdzenie wzrokowe sekcji Cel w wariancie „nowa tabela" (siatka typów,
  kolumna „Podstawa", podgląd DDL, ostrzeżenie o nieodwracalności) w obu paletach — jeśli jeszcze
  się nie odbyło, zrób je PRZED rozpoczęciem I9.

  Jeżeli w trakcie kontekst zacznie się wyczerpywać, zatrzymaj etap w bezpiecznym miejscu,
  zaktualizuj dokumentację i przygotuj handover — nie kończ w połowie bez zapisu stanu.
```
