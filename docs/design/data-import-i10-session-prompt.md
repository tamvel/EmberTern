# Prompt otwierający sesję implementacyjną — etap I10 (Data Import)

Plik jest jednorazowym **materiałem sesyjnym**, nie dokumentem architektonicznym. Treść w bloku niżej
wkleja się jako pierwsza wiadomość nowej sesji. Po zamknięciu I10 plik zastępuje się analogicznym dla I11.

I10 dokłada **dwa źródła o zupełnie różnym ciężarze**: schowek, który jest wyłącznie innym *pochodzeniem*
tekstu i nie powinien kosztować niemal nic, oraz `.xls` (BIFF8), które wymaga **nowej zależności NuGet** i
jest jedynym miejscem w module, gdzie taka decyzja jeszcze zapada. I9 pokazał, że filar „jeden pipeline dla
każdego źródła" się trzyma — I10 jest jego drugim, ostrzejszym testem, bo tym razem dochodzi biblioteka
spoza dotychczasowego zestawu.

---

```
EmberTern — sesja implementacyjna: Data Import, etap I10 (schowek + .xls). I0–I9 są zamknięte.

Zacznij od przeczytania CLAUDE.md i docs/design/data-import.md — blok „📍 STAN IMPLEMENTACJI",
a potem §0 (prawo nadrzędne modułu), §1.5 (odwzorowanie źródeł na providerów), §3.3 (sekcja Źródło
i format + rozgałęzienie po Capabilities), §4.3 (porty) i wiersz I10 w §6. Przeczytaj też
„⭐ I9 as-built" — cztery rzeczy, których nie wolno zepsuć, oraz gotchę #268.

Architektura modułu jest ZAMROŻONA. I10 to wyłącznie implementacja. Odkrycie, które naprawdę
podważa projekt, oznacza: ZATRZYMAJ ETAP I ZGŁOŚ — nigdy cichy redesign.

═══ STAN WEJŚCIOWY ═══
  gałąź feat/data-import, etapy I0–I9 zamknięte, 5763 testy zielone, build 0/0,
  aplikacja startuje czysto. Oba remote'y aktualne.

  ⚠ Testy uruchamiaj DWIEMA PARTYCJAMI i ZAWSZE z instrumentem:
      dotnet test EmberTern.slnx --blame-hang --blame-hang-timeout 120s --filter "FullyQualifiedName!~ConnectionExpandBindingProbe"
      dotnet test EmberTern.slnx --blame-hang --blame-hang-timeout 120s --filter "FullyQualifiedName~ConnectionExpandBindingProbe"
    Zawieszenie #94/#226/#261 wystąpiło w sesji I9; instrument nazwał podejrzanego
    (ConnectionExpandBindingProbe.CompletionRow_HighlightsMatchedPrefix) i pokazał, że wisi
    PO zakończeniu testów, nie w teście. Nic na tej podstawie nie zmieniano — to nie jest zadanie I10.

  ⭐ CAŁA DROGA „ŹRÓDŁO → TABELA (istniejąca LUB nowa)" DZIAŁA DLA TEKSTU I DLA ARKUSZA:
    tools/probes/DataImportProbe     (I4) — 20/20 ALL PASS
    tools/probes/DataImportRunProbe  (I7 + G z I8 + H z I9) — 25/25 ALL PASS na FB5

  Do UŻYCIA, nie do przepisania — po dołożeniu źródeł NIC z tego nie powinno się zmienić:
    ImportPipeline / ImportValueConverter / ImportRowValidator / ImportMappingPlanner
    ImportReadiness / ImportTargetType / ColumnTypeInferencer / ImportNewTable / SourceErrorValue
    FirebirdImportWriter / DryRunImportWriter / PreviewImportWriter / BoundedImportProvider
    DataImportTabViewModel (JEDYNY właściciel ImportConfiguration) + DataImportEnvironment

═══ ZAKRES I10 (z §6) ═══
  1. SCHOWEK. Zgodnie z §1.5 to NIE jest osobny parser — to inne pochodzenie tekstu. App czyta
     schowek (typy Avalonia zostają w App), Core dostaje `string` opakowany w TextImportSource,
     a DelimitedTextImportProvider nie odróżnia go od pliku. Jeżeli schowek wymaga nowego
     providera albo gałęzi w istniejącym — ZATRZYMAJ SIĘ I ZGŁOŚ.
     ⚠ Powierzchnia ma już przełącznik Plik/Schowek i pole ClipboardText; brakuje wyłącznie
     wczytania zawartości schowka z TopLevel (wzorzec: MessageBanner robi to samo dla Copy).
  2. XlsImportProvider (BIFF8) — NOWA ZALEŻNOŚĆ NuGet. I0 zmierzył, że DocumentFormat.OpenXml
     rzuca FileFormatException na prawdziwym .xls, więc trzeba innej biblioteki.
     ⭐ Zależność ląduje WYŁĄCZNIE w EmberTern.Office (jedyne miejsce, gdzie zależność na format
     Office jest dozwolona) i musi być strumieniowa albo mieć jawnie zmierzony koszt pamięci —
     R8 obowiązuje tak samo jak dla .xlsx (I0: DOM = 77× więcej sterty).
  3. Usunięcie z TryCreateSource odmowy dla .xls (zostaje wyłącznie dla formatów bez providera).

═══ ⭐ CO I9 USTALIŁ, A CO OBOWIĄZUJE TAK SAMO DLA .XLS ═══
  • Wartości wychodzą z providera NATYWNE (DateTime, double, bool) — nie tekstem. ConvertNative
    jest już właścicielem tej gałęzi, a ColumnTypeInferencer karmi się przez IImportProvider, więc
    wnioskowanie typów dla NOWEJ tabeli dostajesz za darmo, o ile provider nie oddaje tekstu.
  • Komórka błędu → SourceErrorValue, NIGDY tekst (R20). Nośnik jest źródłowo-neutralny i już
    istnieje; ImportValueConverter odrzuca go PRZED gałęziami typów docelowych, więc kolumna
    tekstowa też go nie przyjmie.
  • Numer wiersza z WŁASNEJ numeracji źródła, nigdy z licznika (§0.6).
  • Wartości umieszczane po współrzędnej komórki, nigdy pozycyjnie (§0.1).
  • Data to liczba PLUS format daty — i uwaga na gotchę #268: kodu formatu nie wolno PRZESZUKIWAĆ,
    trzeba go PARSOWAĆ. SpreadsheetNumberFormats już to robi; jeżeli biblioteka do .xls oddaje
    numFmt w tej samej postaci, użyj tej samej klasy zamiast pisać drugą.
  • Sekcja Format rozgałęzia się po Capabilities, nie po ImportSourceKind. ListSheetsAsync jest na
    porcie — provider .xls po prostu go implementuje i UI działa bez zmian w XAML-u.

═══ WIĄŻĄCE DECYZJE — NIE PODEJMOWAĆ ICH PONOWNIE ═══
  • Reguła #2: żadnych interfejsów bez dwóch implementacji. IImportProvider ma już dwie; .xls jest
    trzecią i niczego nie legalizuje ani nie psuje.
  • Reguła #1: zero typów Avalonia w VM. Schowek czyta App i przekazuje `string`.
  • Reguła #6: kody, nigdy teksty. Jedna powierzchnia komunikatów: MessageBanner.
  • Wiersze idą na linię Data w transakcji modułu (I7.5), CREATE TABLE na linię Ddl (#213).

═══ ⛔ ZAKAZ ZMIAN UI POZA MODUŁEM ═══
  Nie inicjuj globalnych zmian UI ani refaktoryzacji styli. Przebudowa kontrolek Avalonia,
  zagęszczenie interfejsu, responsywność i style to OSOBNY SPRINT UX, świadomie zaplanowany PO
  zakończeniu całego modułu Data Import. Nie dotykaj Themes/ControlStyles.axaml.

═══ DEFINITION OF DONE ═══
  build 0/0 · pełny zestaw testów zielony (baza 5763, dwie partycje) · aplikacja startuje czysto ·
  ⭐ IMPORT ZE SCHOWKA DZIAŁA END-TO-END (wklejenie z Excela to tekst rozdzielany TAB-em — sprawdź
     to na prawdziwym wklejeniu, bo to najczęstszy realny scenariusz tego etapu) ·
  ⭐ IMPORT Z PLIKU .XLS DZIAŁA END-TO-END NA ŻYWEJ BAZIE, do tabeli istniejącej i do NOWEJ ·
  sekcja I w tools/probes/DataImportRunProbe (wzoruj się na H) · pomiar pamięci dla .xls (R8) ·
  commit na feat/data-import · push na origin ORAZ private ·
  aktualizacja bloku „📍 STAN IMPLEMENTACJI" + wiersza I10 w §6 · prompt otwierający I11.

  ⚠ Reguła QA projektu: build + testy + smoke NIE wystarczą, żeby nazwać etap UI zrobionym.
  I10 kończy się zgłoszeniem „implementacja gotowa — oczekuje wizualnego potwierdzenia",
  a etap zamyka dopiero przegląd użytkownika w OBU paletach.

  ⚠ ZALEGŁOŚĆ Z I9: potwierdzenie wzrokowe sekcji Źródło i format w wariancie ARKUSZA (wybór
  arkusza, zniknięte separatory i kodowanie, „traktuj komórki dat jako daty") w obu paletach —
  jeśli jeszcze się nie odbyło, zrób je PRZED rozpoczęciem I10.

  Jeżeli w trakcie kontekst zacznie się wyczerpywać, zatrzymaj etap w bezpiecznym miejscu,
  zaktualizuj dokumentację i przygotuj handover — nie kończ w połowie bez zapisu stanu.
```
