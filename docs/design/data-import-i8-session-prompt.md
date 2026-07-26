# Prompt otwierający sesję implementacyjną — etap I8 (Data Import)

Plik jest jednorazowym **materiałem sesyjnym**, nie dokumentem architektonicznym. Treść w bloku niżej
wkleja się jako pierwsza wiadomość nowej sesji. Po zamknięciu I8 plik zastępuje się analogicznym dla I9.

I8 jest **pierwszym etapem po MVP**: moduł umie już zaimportować CSV/TXT do **istniejącej** tabeli
end-to-end; I8 dokłada drugi wariant celu — **tabelę, której jeszcze nie ma**.

---

```
EmberTern — sesja implementacyjna: Data Import, etap I8 (nowa tabela). MVP jest już zamknięty.

Zacznij od przeczytania CLAUDE.md i docs/design/data-import.md — blok „📍 STAN IMPLEMENTACJI",
a potem sekcje §0 (prawo nadrzędne modułu, zwłaszcza §0.3 i §0.5), §3.4 (sekcja Cel),
§4.5 (model transakcyjny), §4.6 (inwentarz ponownego użycia), §4.8.2 (co należy do
ImportConfiguration) oraz wiersz I8 w §6. Przeczytaj też sekcję „⭐ I7 as-built" — trzy
rzeczy, których nie wolno w I8 zepsuć.

Architektura modułu jest ZAMROŻONA. I8 to wyłącznie implementacja. Odkrycie, które naprawdę
podważa projekt, oznacza: ZATRZYMAJ ETAP I ZGŁOŚ — nigdy cichy redesign.

═══ STAN WEJŚCIOWY ═══
  gałąź feat/data-import, etapy I0–I7 zamknięte, 5607 testów zielonych, build 0/0,
  aplikacja startuje czysto.

  ⭐ CAŁA DROGA „ŹRÓDŁO → ISTNIEJĄCA TABELA" JEST GOTOWA I ZWERYFIKOWANA NA ŻYWYM FB5:
    tools/probes/DataImportProbe     (I4) — 20/20 ALL PASS
    tools/probes/DataImportRunProbe  (I7) — 11/11 ALL PASS (raport == SELECT COUNT(*))

  Do UŻYCIA, nie do przepisania:
    ImportPipeline / DryRunImportWriter / PreviewImportWriter / BoundedImportProvider
    FirebirdImportWriter / FirebirdImportErrorMapper / FirebirdImportTargetPreparer
    BatchedCommitImportWriter / ImportReadiness / ImportMappingPlanner / ImportTargetType
    DataImportTabViewModel (JEDYNY właściciel ImportConfiguration) + DataImportEnvironment

  Powierzchnia (I5–I7) jest kompletna i I8 WSTAWIA się w nią:
    pas E2 (kafelek CEL) trzyma dziś wyłącznie wariant „istniejąca tabela" — świadomie BEZ
      wyłączonego radia „Nowa", bo opcja, która wygląda na wybór i nie prowadzi nigdzie, jest
      kłamstwem, którego pasek gotowości nie umie sprostować. I8 dokłada ten wariant naprawdę.
    ImportConfiguration ma już TargetDescriptor.Kind = NewTable + NewTableColumns — i jest to
      dziś PRZEPUSZCZANE bez zmian przez BuildConfiguration/ApplyConfiguration. Pinuje to test
      „konfiguracja z NOWĄ tabelą jest przepuszczana bez zmian". W I8 ten test zmienia znaczenie:
      od teraz ta sekcja ma właściciela.
    ImportReadiness zna już NewTableHasNoColumns i NewTableWillBeCommitted (ostrzeżenie §0.5).
    ImportBehaviorOptions ma już DropTableOnFailure.

═══ ZAKRES I8 (z §6) ═══
  1. ColumnTypeInferencer (Core, czysty) — ⭐ (I0/REK-7) domyślnie skanuje CAŁE źródło, nie próbkę
     (limit bezpieczeństwa 1 M wierszy), bo w realnym pliku 2 z 5 kolumn były typowo mieszane (R19).
     §0.3: przy JAKIEJKOLWIEK niejednoznaczności wygrywa VARCHAR — nigdy „to chyba liczba".
  2. Siatka typów w sekcji Cel — edytowalna, z ZAWSZE WIDOCZNĄ liczbą przeanalizowanych wierszy
     w kolumnie „Podstawa". Wynik wnioskowania jest POKAZANY I EDYTOWALNY przed wykonaniem DDL.
  3. Podgląd DDL — z TEGO SAMEGO generatora co reszta aplikacji
     (FieldDefinition / TableSpec / DdlGenerator.BuildCreateTable — §4.6 mówi wprost: nic nowego).
  4. Wykonanie CREATE TABLE na linii Ddl (FirebirdDdlExecutor), autonomicznie i z auto-commitem,
     PRZED pierwszym wierszem — gotcha #213.
  5. Ostrzeżenie o nieodwracalności + opcja DROP przy niepowodzeniu (DropTableOnFailure).

═══ WIĄŻĄCE DECYZJE — NIE PODEJMOWAĆ ICH PONOWNIE ═══
  • ⭐⭐ §0.5 / gotcha #213: CREATE TABLE musi być ZATWIERDZONY zanim poleci pierwszy wiersz, bo
    transakcja Firebirda nie może użyć obiektu, którego DDL nie zatwierdziła. WNIOSEK, KTÓRY TRZEBA
    POWIEDZIEĆ WPROST W INTERFEJSIE: **Rollback NIE USUNIE tej tabeli.** Ostrzeżenie stoi tam, gdzie
    zapada decyzja — dokładnie jak w trybie Sequenced Script Executora. Nieatomowość się ujawnia,
    nigdy nie ukrywa.
  • Linie: CREATE TABLE / DROP TABLE → Ddl (autonomiczna, auto-commit, WAIT/Developer Mode).
    Wiersze i DELETE → Data, jedna transakcja robocza użytkownika, auto-begin, NIGDY auto-commit.
    Odczyt katalogu → Metadata. To jest §4.5 i nie podlega zmianie.
  • ⭐ ImportPipeline NIE TWORZY TABELI — to jest jawnie napisane w jego dokumentacji. Tworzenie
    tabeli robi koordynator PRZED przebiegiem. Nie przenoś tego do pipeline'u.
  • §0.3 zachowawcze wnioskowanie: kolumna mieszana ląduje jako VARCHAR, nigdy jako INTEGER
    z bombą zegarową. Nigdy nie zgaduj typu „na oko próbki".
  • §4.8.6 JEDNA REPREZENTACJA: siatka typów produkuje ImportColumnDefinition[] i wchodzi do
    ImportConfiguration.Target.NewTableColumns przez BuildConfiguration/ApplyConfiguration —
    inaczej strażnik refleksyjny wywali build, i ma rację.
    ⚠ DOWODY wnioskowania (ile wierszy przeanalizowano, ile pasowało) NIE należą do konfiguracji:
      to fakty odczytane ze świata, nie decyzje użytkownika (§4.8.2). TYP należy, bo jest edytowalny.
  • §4.6: zero nowego generatora DDL, zero nowego modelu kolumny, zero drugiej siatki.
  • Reguła #6: kody, nigdy teksty. Reguła #1: zero typów Avalonia w VM.
  • Jedna powierzchnia komunikatów: MessageBanner.

═══ ⛔ ZAKAZ ZMIAN UI POZA MODUŁEM ═══
  Nie inicjuj żadnych globalnych zmian UI ani refaktoryzacji styli. Przebudowa kontrolek Avalonia,
  zagęszczenie interfejsu, responsywność i style to OSOBNY SPRINT UX, świadomie zaplanowany PO
  zakończeniu całego modułu Data Import (decyzja użytkownika, CLAUDE.md). Nie dotykaj
  Themes/ControlStyles.axaml. Drobna uwaga dotycząca JEDNEGO ekranu może być poprawiona przy
  okazji; cokolwiek dotykającego całej aplikacji — nie. Najpierw dowozimy moduł.

═══ DEFINITION OF DONE ═══
  build 0/0 · pełny zestaw testów zielony (baza 5607) · aplikacja startuje czysto ·
  ⭐ IMPORT DO NIEISTNIEJĄCEJ TABELI DZIAŁA END-TO-END NA ŻYWEJ BAZIE LABORATORYJNEJ
     (localhost:3050, Lab/EmberTern_Lab.fdb) — tabela powstaje, wiersze wchodzą, liczby w raporcie
     zgadzają się z SELECT COUNT(*), a komunikat o tym, czego Rollback nie cofnie, jest prawdziwy ·
  typy zachowawcze i edytowalne; kolumna mieszana ląduje jako VARCHAR ·
  commit na feat/data-import · push na origin ORAZ private ·
  aktualizacja bloku „📍 STAN IMPLEMENTACJI" + wiersza I8 w §6 w data-import.md ·
  przygotowanie promptu otwierającego I9.

  ⚠ Reguła QA projektu: build + testy + smoke NIE wystarczą, żeby nazwać etap UI zrobionym.
  I8 kończy się zgłoszeniem „implementacja gotowa — oczekuje wizualnego potwierdzenia",
  a etap zamyka dopiero mój przegląd w OBU paletach.

  Jeżeli w trakcie kontekst zacznie się wyczerpywać, zatrzymaj etap w bezpiecznym miejscu,
  zaktualizuj dokumentację i przygotuj handover — nie kończ w połowie bez zapisu stanu.
```
