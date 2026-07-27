# Prompt otwierający sesję implementacyjną — etap I12 (Data Import, domknięcie)

Plik jest jednorazowym **materiałem sesyjnym**, nie dokumentem architektonicznym. Treść w bloku niżej wkleja się
jako pierwsza wiadomość nowej sesji. I12 jest ostatnim etapem modułu, więc ten prompt nie ma następcy — po jego
zamknięciu plik idzie do usunięcia.

I12 różni się od wszystkich poprzednich etapów jeszcze mocniej niż I11. I11 niczego nie dokładał, tylko sprawdzał
rachunek — i rachunek się zgodził. **I12 nie dokłada nawet sprawdzenia: on zamyka.** Cokolwiek w trakcie tej sesji
zacznie wyglądać na nową funkcjonalność, jest sygnałem, że zakres się rozjeżdża, a nie okazją.

Warto też wiedzieć, czego się spodziewać po pomiarze na 1 M wierszy: I0 zmierzył przepustowość na małych
paczkach i wybrał domyślne 500/10 000, ale **nikt nigdy nie przepuścił przez ten moduł miliona wierszy**. To
jedyne miejsce w I12, w którym może wyjść coś niespodziewanego — i jedyne, w którym „wyszło coś niespodziewanego"
jest wynikiem do zaraportowania, a nie do naprawiania po cichu w ostatnim etapie.

---

```
EmberTern — sesja implementacyjna: Data Import, etap I12 (domknięcie modułu). I0–I11 są zamknięte.

Zacznij od przeczytania CLAUDE.md i docs/design/data-import.md — blok „📍 STAN IMPLEMENTACJI",
potem §0 (prawo nadrzędne modułu), wiersz I12 w §6, oraz bloki „⭐ I11 as-built" i „⭐ I10 as-built".
Przeczytaj też §3.8 — tam mieszkają uwagi z przeglądów wzrokowych i to jest wejście do audytu UI.

Architektura modułu jest ZAMROŻONA i etap I12 jej nie dotyka. To jest DOMKNIĘCIE.

═══ STAN WEJŚCIOWY ═══
  gałąź feat/data-import, etapy I0–I11 zamknięte, 5835 testów zielonych, build 0/0,
  aplikacja startuje czysto.

  ⚠ Testy uruchamiaj DWIEMA PARTYCJAMI i ZAWSZE z instrumentem:
      dotnet test EmberTern.slnx --blame-hang --blame-hang-timeout 120s --filter "FullyQualifiedName!~ConnectionExpandBindingProbe"
      dotnet test EmberTern.slnx --blame-hang --blame-hang-timeout 120s --filter "FullyQualifiedName~ConnectionExpandBindingProbe"
    Zawieszenie #94/#226/#261 to OSOBNE zadanie infrastrukturalne — nie podejmuj go tutaj.

  ⚠ Zanim zbudujesz: zabij ewentualny działający EmberTern.exe (blokuje DLL-e, MSB3021).

  Weryfikacja na żywo, która już istnieje i której NIE piszemy od nowa:
    tools/probes/DataImportProbe     (I4) — 20/20 ALL PASS
    tools/probes/DataImportRunProbe  (I7 + G z I8 + H z I9 + I z I10) — 33/33 ALL PASS na FB5

═══ ZAKRES I12 (z §6) ═══
  (a) DOKUMENTACJA: narracja modułu do docs/history/ (nowy plik tematyczny — moduł nie ma jeszcze
      swojego), wpisy do docs/gotchas.md, aktualizacja CLAUDE.md W MIEJSCU (krótko, w czasie
      teraźniejszym — nie dopisuj kolejnego bloku narracyjnego, to jest dokładnie ten nawyk, który
      kiedyś rozdął CLAUDE.md).
  (b) AUDYT UI: obie palety, checklista z sekcji „UI Review Checklist" w CLAUDE.md, wszystkie
      powierzchnie modułu (źródło, cel, mapowanie, podgląd, raport, pasek gotowości, pasek poleceń
      z selektorem profili, dialogi potwierdzeń i dialog nazwy profilu).
  (c) POMIAR WYDAJNOŚCI na 1 M wierszy.

  ⛔ ZERO nowej funkcjonalności. Jeśli audyt znajdzie brak, który wymaga nowej możliwości, to jest
     pozycja do ZAPISANIA w dokumencie, nie do zbudowania w etapie domykającym.

═══ ⭐ POMIAR NA 1 M WIERSZY — CO WŁAŚCIWIE MA POKAZAĆ ═══
  • To jest POMIAR, nie optymalizacja. Wynik gorszy od oczekiwań jest wynikiem; przebudowa writera
    w etapie domykającym nie jest.
  • Zmierz KSZTAŁT KRZYWEJ, nie tylko liczbę końcową — to lekcja z R8 w I10, gdzie płaska sterta
    przez 60 000 wierszy była dowodem, a 26,7 MB tylko liczbą. Sterta rosnąca liniowo z liczbą
    wierszy znaczy, że coś materializuje źródło.
  • Sprawdź jednocześnie, że raport pozostaje uczciwy przy tej skali: numery wierszy źródłowych
    (§0.6), przycięcie listy błędów, licznik postępu.
  • Domyślne 500 / 10 000 pochodzą z I0 i były mierzone na małych zbiorach. Jeżeli 1 M pokaże, że
    są złe — ZGŁOŚ to z liczbami; zmiana domyślnych jest decyzją użytkownika.
  • Sonda jednorazowa w tools/probes/, poza solution, do usunięcia po zamknięciu etapu — tak jak
    XlsFormatProbe w I10.

═══ ⭐ AUDYT UI — CZEGO SZUKAĆ, A CZEGO NIE ═══
  • Szukaj: twardych kolorów (tylko tokeny motywu), lokalnych definicji pędzli, {StaticResource}
    tam gdzie ma być {DynamicResource}, zduplikowanych stylów, powierzchni które nie czytają się
    w jednej z palet.
  • ⛔ NIE szukaj gęstości kontrolek. Wysokość TextBoxa/ComboBoxa/Buttona to OSOBNY, ŚWIADOMIE
    ZAPLANOWANY SPRINT UX PO CAŁYM MODULE (U4 w §3.8, dyrektywa użytkownika z 2026-07-26).
    NIE dotykaj Themes/ControlStyles.axaml.
  • Robocza linia podziału, ustalona przez użytkownika: „wysokość kontrolki" to sprint,
    „dwie kontrolki jedna pod drugą zamiast obok siebie" to moduł.

═══ ⭐ CO JEST OTWARTE I MA ZOSTAĆ ZAPISANE, NIE ZAŁATWIONE ═══
  • U4 (gęstość globalna) i U5 (responsywność) z §3.8 — do sprintu UX.
  • Eksport/import profilu do .json — świadomie pominięty w I11 jako opcjonalny; jeżeli ma wrócić,
    to jako osobna, mała pozycja, nie w etapie domykającym.
  • Znacznik „zmodyfikowany" przy wybranym profilu — świadomie pominięty w I11 (wymagałby
    kanonicznego porównania dwóch ImportConfiguration, czyli zmiany modelu).
  • Audyt cichej utraty znaków spoza charsetu połączenia — OSOBNE zadanie platformowe, nie moduł.
  • Zawieszenie pełnego zestawu testów (#94/#226/#261) — osobne zadanie infrastrukturalne.

═══ WIĄŻĄCE DECYZJE — NIE PODEJMOWAĆ ICH PONOWNIE ═══
  • Reguła #1: zero typów Avalonia i zero typów Firebirda w VM.
  • Reguła #6: kody, nigdy teksty. Jedna powierzchnia komunikatów: MessageBanner.
  • Rozdzielne pushe na origin i private (izolacja awarii — wariant z dwoma pushurl odrzucony).

═══ DEFINITION OF DONE ═══
  build 0/0 · pełny zestaw testów zielony (baza 5835, dwie partycje) · aplikacja startuje czysto ·
  narracja modułu w docs/history/ + wpisy w docs/gotchas.md + CLAUDE.md zaktualizowany W MIEJSCU ·
  audyt UI przeprowadzony w OBU paletach z jawną listą tego, co sprawdzono ·
  ⭐ POMIAR NA 1 M WIERSZY z liczbami i kształtem krzywej, oraz jawnym stwierdzeniem, czy domyślne
     500 / 10 000 się bronią ·
  ⭐ JAWNA LISTA TEGO, CO ZOSTAJE OTWARTE po zamknięciu modułu (z powodem przy każdej pozycji) ·
  commit na feat/data-import · push na origin ORAZ private ·
  blok „📍 STAN IMPLEMENTACJI" domknięty · docs/design/data-import-i12-session-prompt.md usunięty
  (nie ma następnego etapu) · propozycja scalenia feat/data-import do master przedstawiona
  użytkownikowi jako DECYZJA DO PODJĘCIA, nie wykonana samodzielnie.

  ⚠ Reguła QA projektu: build + testy + smoke NIE wystarczą, żeby nazwać etap UI zrobionym.
  I12 kończy się zgłoszeniem „domknięcie gotowe — oczekuje przeglądu użytkownika",
  a moduł zamyka dopiero potwierdzenie użytkownika.

  ⛔ DYREKTYWA UŻYTKOWNIKA, NADAL OBOWIĄZUJĄCA: nie dokładamy refaktorów ani zmian
  architektonicznych. Jeżeli coś wygląda na wymagające zmiany architektury — to jest przypadek
  „ZATRZYMAJ I ZGŁOŚ", a nie zaproszenie do przeprojektowania w ostatnim etapie.

  Jeżeli w trakcie kontekst zacznie się wyczerpywać, zatrzymaj etap w bezpiecznym miejscu,
  zaktualizuj dokumentację i przygotuj handover — nie kończ w połowie bez zapisu stanu.
```
