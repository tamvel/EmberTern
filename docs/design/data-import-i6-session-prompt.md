# Prompt otwierający sesję implementacyjną — etap I6 (Data Import)

Plik jest jednorazowym **materiałem sesyjnym**, nie dokumentem architektonicznym. Treść w bloku niżej
wkleja się jako pierwsza wiadomość nowej sesji. Po zamknięciu I6 plik zastępuje się analogicznym dla I7.

Kontekst, którego prompt nie powtarza, bo sesja i tak go czyta: `CLAUDE.md` →
`docs/design/data-import.md` (blok „📍 STAN IMPLEMENTACJI" + wiersz I6 w §6 + **§3.8**).

---

```
EmberTern — sesja implementacyjna: Data Import, etap I6.

Zacznij od przeczytania CLAUDE.md i docs/design/data-import.md — blok „📍 STAN IMPLEMENTACJI",
sekcje §3.4 (Cel), §3.5 (Mapowanie), §4.7 (przeliczanie łańcuchowe), §4.8 (konfiguracja) oraz
§3.8 (otwarte uwagi UX po przeglądzie I5). Architektura modułu jest ZAMROŻONA — I6 to wyłącznie
implementacja. Odkrycie, które naprawdę podważa projekt, oznacza: ZATRZYMAJ ETAP I ZGŁOŚ,
nigdy cichy redesign.

Stan wejściowy:
  gałąź feat/data-import, ostatni commit = szew domykający I5, 5567 testów zielonych, build 0/0.
  Po I4 cały silnik jest gotowy i zweryfikowany na żywym FB5 — I6 to praca wyłącznie w App.

✅ Układ powierzchni jest już rozstrzygnięty i zbudowany (§3.1 po rewizji + §3.8). I6 WSTAWIA się
   w gotową ramę, nie przebudowuje jej:
     • wiersz Grid.Row=4 czeka pusty na kafelek CEL (E2) — jeden wiersz w stanie ustalonym:
       picker tabeli zawsze żywy + pod nim linia faktów, dokładnie jak kafelek ŹRÓDŁO,
     • wiersz Grid.Row=5 to JEDYNA gwiazdka na powierzchni i czeka na Mapowanie | Podgląd;
       dziś trzyma stan pusty z UiStrings.ImportWorkAreaEmpty — zastąp go treścią,
     • pas gotowości, splitter dolnego panelu i pas H są gotowe i nie wymagają zmian.
   ⛔ Nie zmieniaj wysokości kontrolek ani Themes/ControlStyles.axaml — globalna gęstość to
      osobny sprint UX PO zamknięciu modułu (§3.8/U4), decyzja użytkownika.
   ⚠ U5 (responsywność) weryfikuje się właśnie teraz: gdy Cel i Mapowanie zajmą miejsce,
      sprawdź na 1920×1080 i zgłoś, jeśli proporcje wymagają korekty — z propozycją, nie po cichu.

Zakres I6 (z §6):
  1. Sekcja CEL — istniejąca tabela (§3.4): wybór tabeli przez istniejący SearchableComboBox,
     lista tabel z linii METADATA (read-only), fakty o tabeli (liczba kolumn, rekordów, klucz
     główny, aktywne triggery BEFORE INSERT — czytane przez gotowy FirebirdImportTargetReader),
     opcja „Opróżnij tabelę przed importem". Wariant „Nowa tabela" NIE jest w zakresie — to I8.
  2. Panel MAPOWANIE (§3.5): orientacja CEL → ŹRÓDŁO (wierszem jest kolumna tabeli, bo to ona ma
     wymagania), auto-mapowanie po nazwie z gotowego ImportMappingPlanner, diagnostyka
     (przewidywanie z próbki, ZAWSZE z liczbą wierszy), kolumny systemowe ZABLOKOWANE, NIE
     UKRYTE, i mówiące dlaczego (COMPUTED BY nigdy; identity ALWAYS tylko po jawnym odblokowaniu),
     pochodzenie mapowania renderowane jak ValueOrigin w debuggerze, lista niewykorzystanych pól
     źródłowych.
  3. Przeliczanie łańcuchowe z anulowaniem (§4.7) rozszerzone o cel i mapowanie.

Wiążące zasady tego etapu:
  • DataImportTabViewModel pozostaje JEDYNYM właścicielem ImportConfiguration. Każde nowe
    ustawienie przechodzi przez BuildConfiguration/ApplyConfiguration (§4.8.6) — inaczej strażnik
    refleksyjny ImportConfigurationRoundTripTests wywali build, i ma rację.
  • Sekcje, których nadal nie ma, MUSZĄ być przepuszczane bez zmian (starszy build nie okrada
    profilu nowszego) — to już działa, nie zepsuj tego.
  • ZERO logiki decyzyjnej w App. Mapowanie planuje ImportMappingPlanner, gotowość liczy
    ImportReadiness, typ kolumny zna ImportTargetType. VM projektuje, nie rozstrzyga. Druga
    analiza po stronie UI to dokładnie sposób, w jaki siatka i pasek gotowości zaczynają mówić
    użytkownikowi co innego.
  • Reguła #6: kody, nigdy teksty. Nowy komunikat = kod w ImportDiagnostics + zdanie w UiStrings.
  • Reguła #1: zero typów Avalonia w VM (pędzel przez klucz + IconBrushConverter, z wariantem
    motywu — gotcha #250).
  • Jedna powierzchnia komunikatów: MessageBanner. Zero lokalnie kolorowanych napisów.
  • Odczyt metadanych idzie linią Metadata (read-only, transakcje niejawne). Nic w I6 nie pisze
    do bazy.

Definition of Done:
  build 0/0 · pełny zestaw testów zielony (baza 5559) · aplikacja startuje czysto ·
  mapowanie ręczne i automatyczne działa na żywej bazie laboratoryjnej ·
  niezgodności widoczne PRZED importem · commit na feat/data-import ·
  push na origin ORAZ private · aktualizacja bloku „📍 STAN IMPLEMENTACJI" w data-import.md.

  ⚠ Reguła QA projektu: build + testy + smoke NIE wystarczą, żeby nazwać etap UI zrobionym.
  I6 kończy się zgłoszeniem „implementacja gotowa — oczekuje wizualnego potwierdzenia",
  a etap zamyka dopiero mój przegląd w OBU paletach.
```
