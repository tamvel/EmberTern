# Prompt otwierający sesję implementacyjną — etap I7 (Data Import)

Plik jest jednorazowym **materiałem sesyjnym**, nie dokumentem architektonicznym. Treść w bloku niżej
wkleja się jako pierwsza wiadomość nowej sesji. Po zamknięciu I7 plik zastępuje się analogicznym dla I8.

I7 jest **ostatnim etapem MVP** — po nim moduł umie zaimportować CSV/TXT do istniejącej tabeli
end-to-end, z walidacją, raportem i pamięcią ostatniej konfiguracji.

---

```
EmberTern — sesja implementacyjna: Data Import, etap I7. To OSTATNI etap MVP.

Zacznij od przeczytania CLAUDE.md i docs/design/data-import.md — blok „📍 STAN IMPLEMENTACJI",
a potem sekcje §0 (prawo nadrzędne modułu), §3.6 (Podgląd po konwersji), §3.7 (uruchomienie,
postęp, raport), §4.4 (pipeline), §4.5 (model transakcyjny — najważniejsza decyzja modułu),
§4.8 (konfiguracja i profile) oraz §3.1 (układ powierzchni po rewizji).

Architektura modułu jest ZAMROŻONA. I7 to wyłącznie implementacja. Odkrycie, które naprawdę
podważa projekt, oznacza: ZATRZYMAJ ETAP I ZGŁOŚ — nigdy cichy redesign.

═══ STAN WEJŚCIOWY ═══
  gałąź feat/data-import (wypchnięta na origin ORAZ private), etapy I0–I6 zamknięte,
  5583 testy zielone, build 0/0, aplikacja startuje czysto.

  ⭐ CAŁY SILNIK JEST GOTOWY I ZWERYFIKOWANY NA ŻYWYM FB5 od etapu I4
  (tools/probes/DataImportProbe — 20/20 ALL PASS). I7 nie pisze silnika; I7 go URUCHAMIA
  z interfejsu i pokazuje wynik. Gotowe i przetestowane, do UŻYCIA, nie do przepisania:

    ImportPipeline.RunAsync(configuration, target, provider, source, writer,
                            connectionEncoding, progress, cancellationToken) -> ImportOutcome
        — JEDEN import. Nie wie, CO czyta ani CZY pisze. Właściciel okna
          „indeks w paczce → numer wiersza źródłowego" (raport NIGDY nie widzi indeksu paczki).
          NIE kończy transakcji i NIE tworzy tabeli.
    DryRunImportWriter          — „Waliduj" to INNY ARGUMENT, nie inny tryb. Nie buduj drugiej
                                  ścieżki walidacji: jedyne, co gwarantuje, że „Waliduj mówi OK"
                                  coś znaczy, to fakt, że biegnie tym samym pipeline'em.
    FirebirdImportWriter(transactionService, errorPolicy)
                                — FbBatchCommand, paczki po 500, MultiError = polityka błędów 1:1,
                                  OVERRIDING SYSTEM VALUE dla zmapowanej identity ALWAYS,
                                  linia Data, auto-begin, NIGDY auto-commit.
    FirebirdImportErrorMapper   — FbException → ImportErrorKind z WEKTORA GDS, nigdy z tekstu.
    ImportOutcome               — Rows*, Errors, ⚠ Warnings + WarningsTruncated (wiersz SKRÓCONY
                                  to ostrzeżenie z oryginałem, nie błąd — nie zawyża RowsFailed).
    ImportValueConverter / ImportRowValidator (+ ImportCharsetGuard) / ImportMappingPlanner.Project
    ImportReadiness             — gotowość; już czyta wybrany cel i mapowanie.

  Powierzchnia (I5+I6) też jest gotowa i I7 WSTAWIA się w nią:
    DataImportTabView.axaml — Grid.Row=1 czeka PUSTY na pas B (pasek poleceń).
    Grid.Row=5 to JEDYNA gwiazdka; dziś trzyma sam panel Mapowania — dołóż po prawej
      Podgląd po konwersji i rozdziel je pionowym GridSplitterem (§3.1 pas F).
    Pas G (dolny panel) ma splitter, zwijanie i TRWAŁĄ wysokość; dziś ma jedną zakładkę
      „Podgląd źródła" — dołóż „Błędy" i „Raport" jako kolejne zakładki tego samego panelu.
    Pas H mówi już, dokąd wiersze idą (połączenie + linia Data) — dołóż tryb transakcji.
    DataImportTabViewModel jest JEDYNYM właścicielem ImportConfiguration.

═══ ZAKRES I7 (z §6) ═══
  1. PODGLĄD PO KONWERSJI (§3.6) — ciągły, PO konwersji, bo to jedyna informacja, która ma
     znaczenie: co naprawdę trafi do bazy. Zmiana separatora dziesiętnego natychmiast widać
     (debounce ~150 ms). Wiersz z błędem: marker + WARTOŚĆ SUROWA. Siatka jak wszędzie
     (GridProfile, „Rekord N z M", kopiowanie).
     ⛔ Panel filtrów i pasek agregacji świadomie NIE są podpinane — filtr sugerowałby wpływ na
        import. To granica zakresu, nie przeoczenie (§3.6).
  2. PASEK POLECEŃ (pas B): Importuj (Classes="primary", F5) · Waliduj (Ctrl+F5) · Anuluj
     (widoczny tylko w trakcie, Esc) · tryb transakcji · polityka błędów · ExecutionTimer
     dokowany po prawej (nie przesuwa przycisków — wzorzec ze Script Executora).
  3. URUCHOMIENIE: postęp dławiony (~200 wierszy / 100 ms, IProgress), anulowanie,
     sekcje konfiguracyjne w trakcie TYLKO DO ODCZYTU (nie wyszarzone do nieczytelności —
     konfiguracja tłumaczy, co się dzieje).
  4. RAPORT (dolna zakładka, aktywowana automatycznie po zakończeniu): N z M, tabela błędów
     (wiersz · kolumna · wartość · powód), podwójne kliknięcie → wiersz w podglądzie,
     „Eksportuj raport…" przez ISTNIEJĄCY framework eksportu (lista błędów jako
     IExportDataSource → CSV/XLSX/schowek za darmo, zero nowej serializacji), Kopiuj.
     ⭐ Commit / Rollback SĄ W RAPORCIE, nie tylko w globalnym pasku transakcji — decyzja zapada
        tam, gdzie są liczby.
  5. „OSTATNIO UŻYTA" KONFIGURACJA: zapis przy starcie importu i odtworzenie przy otwarciu
     zakładki, przez GOTOWY ImportProfileStore (GetLastUsed/SaveLastUsed). Zero nowych modeli.
  6. Zaległość z I6 do domknięcia TUTAJ: liczba rekordów tabeli docelowej. Czytana RAZ, przy
     starcie, wyłącznie po to, by potwierdzić „opróżnij tabelę przed importem" („zaraz skasujesz
     N wierszy"). Świadomie NIE czytamy jej przy każdej zmianie celu (SELECT COUNT(*)).

═══ WIĄŻĄCE DECYZJE — NIE PODEJMOWAĆ ICH PONOWNIE ═══
  • MODEL TRANSAKCYJNY (§4.5) — najważniejsza decyzja modułu:
      wiersze INSERT  → linia Data, JEDNA transakcja robocza użytkownika,
                        auto-begin, NIGDY auto-commit (reguła #3 + gotcha #89)
      DELETE FROM     → ta sama transakcja (to dane, nie schemat — ma być wycofywalne)
      odczyt metadanych → linia Metadata (read-only)
      CREATE TABLE    → linia Ddl (I8, gotcha #213)
    Domyślny tryb: Manual. Batched NIGDY nie jest domyślny i zawsze niesie opis skutku.
  • §0.6 RAPORT NIE KŁAMIE: „zaimportowano N" znaczy N przyjętych przez serwer. Gdy transakcja
    zostaje otwarta, raport mówi „N wierszy wstawionych — transakcja otwarta, zatwierdź lub
    wycofaj", a NIE „import zakończony powodzeniem".
  • §0.5 TRANSAKCJA MÓWI PRAWDĘ: przed startem widać, co zostanie zatwierdzone i czego Rollback
    nie cofnie (wartości generatorów, skutki triggerów, zatwierdzone paczki w trybie Batched).
  • §4.8.6 JEDNA REPREZENTACJA: każde nowe ustawienie przechodzi przez
    BuildConfiguration/ApplyConfiguration — inaczej strażnik refleksyjny
    ImportConfigurationRoundTripTests wywali build, i ma rację. Sekcje, których nadal nie ma
    (nowa tabela — I8), MUSZĄ być przepuszczane bez zmian.
    ⚠ Wysokość paneli i stan zwinięcia NIE należą do ImportConfiguration — to preferencje
      układu, mieszkają w WorkspaceState (§4.8.2).
  • ZERO logiki decyzyjnej w App: pipeline liczy, ImportReadiness bramkuje, ImportTargetType zna
    typy. VM projektuje, nie rozstrzyga.
  • Reguła #6: kody, nigdy teksty (ImportDiagnostics + UiStrings).
  • Reguła #1: zero typów Avalonia w VM.
  • Jedna powierzchnia komunikatów: MessageBanner. Zero lokalnie kolorowanych napisów.
  • Identity GENERATED ALWAYS: reguły z I6 zostają BEZ ZMIAN (reguła jedynej pary może ją
    sparować, Core podnosi IMP0007, writer emituje OVERRIDING SYSTEM VALUE). Nie „naprawiaj" Core.

═══ ⛔ ZAKAZ ZMIAN UI POZA MODUŁEM ═══
  Nie inicjuj żadnych globalnych zmian UI ani refaktoryzacji styli. Przebudowa kontrolek
  Avalonia, zagęszczenie interfejsu, responsywność i style to OSOBNY SPRINT UX, świadomie
  zaplanowany PO zakończeniu całego modułu Data Import (decyzja użytkownika, CLAUDE.md).
  Nie dotykaj Themes/ControlStyles.axaml. Drobna uwaga dotycząca JEDNEGO ekranu może być
  poprawiona przy okazji; cokolwiek dotykającego całej aplikacji — nie.
  Najpierw dowozimy moduł.

═══ DEFINITION OF DONE ═══
  build 0/0 · pełny zestaw testów zielony (baza 5583) · aplikacja startuje czysto ·
  ⭐ IMPORT CSV → ISTNIEJĄCA TABELA DZIAŁA END-TO-END NA ŻYWEJ BAZIE LABORATORYJNEJ
     (localhost:3050, Lab/EmberTern_Lab.fdb) — liczby w raporcie zgadzają się z SELECT COUNT(*) ·
  druga sesja startuje z przywróconą konfiguracją ·
  commit na feat/data-import · push na origin ORAZ private ·
  aktualizacja bloku „📍 STAN IMPLEMENTACJI" + wiersza I7 w §6 w data-import.md ·
  przygotowanie promptu otwierającego I8.

  ⚠ Reguła QA projektu: build + testy + smoke NIE wystarczą, żeby nazwać etap UI zrobionym.
  I7 kończy się zgłoszeniem „implementacja gotowa — oczekuje wizualnego potwierdzenia",
  a etap zamyka dopiero mój przegląd w OBU paletach.

  Jeżeli w trakcie kontekst zacznie się wyczerpywać, zatrzymaj etap w bezpiecznym miejscu,
  zaktualizuj dokumentację i przygotuj handover — nie kończ w połowie bez zapisu stanu.
```
