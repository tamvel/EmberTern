# Prompt otwierający sesję implementacyjną — etap I11 (Data Import)

Plik jest jednorazowym **materiałem sesyjnym**, nie dokumentem architektonicznym. Treść w bloku niżej
wkleja się jako pierwsza wiadomość nowej sesji. Po zamknięciu I11 plik zastępuje się analogicznym dla I12.

I11 jest inny niż wszystkie poprzednie etapy i warto wiedzieć, dlaczego, zanim się zacznie. Osiem etapów
dokładało możliwości; **ten jeden niczego nie dokłada — on sprawdza rachunek.** §4.8 postawiło tezę, że
nazwane profile są *konsekwencją* modelu, a nie funkcją do dobudowania, i wprost napisało, jaki dowód tę tezę
obali: „jeżeli nazwane profile wymagają zmiany choćby jednego modelu albo przebudowy sekcji UI, znaczy że §4.8
zostało naruszone po drodze". Sesja I11 ma więc jeden nietypowy obowiązek — **jeżeli okaże się, że trzeba
ruszyć modele albo pipeline, to jest wynik do zaraportowania, a nie przeszkoda do obejścia.**

Dwa poprzednie etapy dostarczyły mocnych poszlak, że rachunek się zgadza: I9 dołożył źródło, a I10 dołożył
źródło **razem z nową zależnością NuGet** — i w obu przypadkach pipeline, konwerter, walidator, planer
mapowania i writer pozostały nietknięte. I10 przyniósł też ostrzeżenie odwrotnego rodzaju: schowek z listy
zakresu okazał się **już zbudowany**. Zanim napiszesz cokolwiek z zakresu I11, sprawdź, co już stoi.

---

```
EmberTern — sesja implementacyjna: Data Import, etap I11 (nazwane profile). I0–I10 są zamknięte.

Zacznij od przeczytania CLAUDE.md i docs/design/data-import.md — blok „📍 STAN IMPLEMENTACJI",
a potem §0 (prawo nadrzędne modułu), §4.8 W CAŁOŚCI (to jest architektura tego etapu — 4.8.1 zasada,
4.8.2 zakres rekordu, 4.8.3 magazyn, 4.8.4 co powstało w MVP i gdzie ZAREZERWOWANO miejsce w pasku
poleceń, 4.8.5 wczytanie profilu nie może przemilczeć zmiany, 4.8.6 mechanizm chroniący przed
przebudową) oraz wiersz I11 w §6. Przeczytaj też „⭐ I10 as-built" i „⭐ I9 as-built".

Architektura modułu jest ZAMROŻONA. I11 to wyłącznie implementacja. Odkrycie, które naprawdę
podważa projekt, oznacza: ZATRZYMAJ ETAP I ZGŁOŚ — nigdy cichy redesign.

═══ STAN WEJŚCIOWY ═══
  gałąź feat/data-import, etapy I0–I10 zamknięte, 5785 testów zielonych, build 0/0,
  aplikacja startuje czysto. Oba remote'y aktualne.

  ⚠ Testy uruchamiaj DWIEMA PARTYCJAMI i ZAWSZE z instrumentem:
      dotnet test EmberTern.slnx --blame-hang --blame-hang-timeout 120s --filter "FullyQualifiedName!~ConnectionExpandBindingProbe"
      dotnet test EmberTern.slnx --blame-hang --blame-hang-timeout 120s --filter "FullyQualifiedName~ConnectionExpandBindingProbe"
    Zawieszenie #94/#226/#261 to OSOBNE zadanie infrastrukturalne — nie podejmuj go wewnątrz etapu modułu.

  ⭐ CAŁA DROGA DZIAŁA DLA TRZECH ŹRÓDEŁ I DWÓCH WARIANTÓW CELU:
    tools/probes/DataImportProbe     (I4) — 20/20 ALL PASS
    tools/probes/DataImportRunProbe  (I7 + G z I8 + H z I9 + I z I10) — 33/33 ALL PASS na FB5

  ⭐⭐ TO WSZYSTKO JUŻ ISTNIEJE I I11 MA TEGO UŻYĆ, NIE NAPISAĆ PONOWNIE:
    ImportConfiguration      — jedyna reprezentacja wszystkich decyzji (§4.8.1)
    ImportProfile            — Id / Name / ConnectionId / CreatedUtc / LastUsedUtc / Configuration
    ImportProfileStore       — fasada sekcji nad zaszyfrowanym settings.dat (wzorzec WatchStore)
    BuildConfiguration() / ApplyConfiguration()  — JEDYNY punkt tłumaczenia stan UI ⇄ rekord (§4.8.6)
    ImportConfigurationRoundTripTests + test refleksyjny — psuje build, gdy ustawienie omija obieg
    DataImportEnvironment    — LoadLastUsed / SaveLastUsed już przechodzą tą samą ścieżką

  ⚠ ZANIM NAPISZESZ COKOLWIEK: sprawdź, ile z tego już stoi. W I10 pozycja „schowek" z listy zakresu
    okazała się zbudowana w całości (przełącznik, pole, komenda, delegat, odczyt z TopLevel) — etap
    dołożył jej wyłącznie dowody. Kwadrans na przeczytanie ImportProfileStore i paska poleceń
    oszczędzi napisania czegoś, co już działa.

═══ ZAKRES I11 (z §6) ═══
  Selektor profili w ZAREZERWOWANYM miejscu paska poleceń (§4.8.4: skrajnie lewa pozycja pasa B, przed
  „Importuj" — miejsce zostało zostawione właśnie po to, żeby dodanie selektora nie przebudowało
  toolbara), „Zapisz jako…", zmiana nazwy, usuwanie, opcjonalny eksport/import `.json`.

  ⭐⭐ ZERO ZMIAN W MODELACH I W PIPELINE. To nie jest ambicja — to jest kryterium odbioru etapu.
     §6 mówi wprost: „I11 jest dowodem projektu". Jeżeli w trakcie okaże się, że trzeba dotknąć
     ImportConfiguration, ImportProfile, ImportProfileStore albo pipeline'u — ZATRZYMAJ SIĘ I ZGŁOŚ,
     bo to znaczy, że §4.8 zostało po drodze naruszone i użytkownik ma o tym wiedzieć.

═══ ⭐ CO MUSI ZADZIAŁAĆ SAMO, JEŚLI PROJEKT JEST DOBRY ═══
  • Wczytanie profilu idzie DOKŁADNIE tą samą ścieżką co przywrócenie „ostatnio użytej" konfiguracji
    (§4.8.5): ApplyConfiguration → odczyt świata na nowo → ImportMappingPlanner → pasek gotowości.
    Wczytanie profilu NIE MA własnej logiki mapowania. Jeśli piszesz drugą — to jest sygnał alarmowy.
  • Niezgodność ze światem (plik zniknął, tabela ma nową kolumnę NOT NULL, arkusz ma inne pola) NIE
    JEST wyjątkiem do złapania — jest pozycją w pasku gotowości (§0.7). Profil nie może być cichym
    sprawcą złego importu.
  • Profil przechowuje DECYZJE, nigdy danych: żadnego wiersza, żadnej zawartości schowka, żadnego
    rozwiązanego SourceSchema ani ColumnSpec[] celu (§4.8.2). Zapinowane już testem — nie osłabiaj go.
  • Wersji kontenera settings.dat NIE RUSZAMY (§4.8.3, nauka z C3: podbicie uruchamia ochronę przed
    downgrade'em i starsza aplikacja odmawia odczytu CAŁEGO pliku). Wersjonowanie żyje wewnątrz
    ImportConfiguration.Version; nieznana wyższa wersja ⇒ profil nieczytelny Z KOMUNIKATEM, nigdy
    wczytany po części.
  • ConnectionId: null ⇒ profil przenośny między połączeniami. Rozstrzygnij widocznie, czy selektor
    pokazuje profile innych połączeń — i cokolwiek wybierzesz, powiedz to użytkownikowi na ekranie.

═══ WIĄŻĄCE DECYZJE — NIE PODEJMOWAĆ ICH PONOWNIE ═══
  • Reguła #1: zero typów Avalonia w VM. Zapis/odczyt pliku .json robi App i podaje string/ścieżkę.
  • Reguła #6: kody, nigdy teksty. Jedna powierzchnia komunikatów: MessageBanner.
  • Usunięcie profilu jest destrukcyjne ⇒ potwierdzenie (wzorzec: ConfirmDialog z „opróżnij tabelę").
  • Trwałość ma JEDNEGO właściciela — koordynator, nigdy VM-y sekcji (§4.8.6).

═══ ⛔ ZAKAZ ZMIAN UI POZA MODUŁEM ═══
  Nie inicjuj globalnych zmian UI ani refaktoryzacji styli. Przebudowa kontrolek Avalonia,
  zagęszczenie interfejsu, responsywność i style to OSOBNY SPRINT UX, świadomie zaplanowany PO
  zakończeniu całego modułu Data Import. Nie dotykaj Themes/ControlStyles.axaml.

═══ DEFINITION OF DONE ═══
  build 0/0 · pełny zestaw testów zielony (baza 5785, dwie partycje) · aplikacja startuje czysto ·
  ⭐ NAZWANY PROFIL ODTWARZA CAŁY IMPORT — zapisz konfigurację, zamknij zakładkę, otwórz, wczytaj
     profil, naciśnij F5 i dostań ten sam wynik ·
  ⭐ NIEZGODNOŚCI RAPORTOWANE PRZEZ PASEK GOTOWOŚCI, nie wyjątkiem: sprawdź na żywo profil, którego
     plik usunięto, i profil wskazujący tabelę, której już nie ma ·
  ⭐ JAWNE STWIERDZENIE W RAPORCIE Z SESJI, czy dało się to zrobić bez zmian w modelach i pipeline —
     to jest właściwy wynik tego etapu, ważniejszy od samej funkcji ·
  commit na feat/data-import · push na origin ORAZ private ·
  aktualizacja bloku „📍 STAN IMPLEMENTACJI" + wiersza I11 w §6 · prompt otwierający I12.

  ⚠ Reguła QA projektu: build + testy + smoke NIE wystarczą, żeby nazwać etap UI zrobionym.
  I11 kończy się zgłoszeniem „implementacja gotowa — oczekuje wizualnego potwierdzenia",
  a etap zamyka dopiero przegląd użytkownika w OBU paletach.

  ⛔ DYREKTYWA UŻYTKOWNIKA WYDANA PRZY ZAMYKANIU I9, NADAL OBOWIĄZUJĄCA: nie dokładamy nowych
  refaktorów ani zmian architektonicznych. Moduł ma zostać konsekwentnie dowieziony do końca zgodnie
  z zamrożonym projektem. Jeżeli coś wygląda na wymagające zmiany architektury — to jest właśnie
  przypadek „ZATRZYMAJ ETAP I ZGŁOŚ", a nie zaproszenie do przeprojektowania.

  Jeżeli w trakcie kontekst zacznie się wyczerpywać, zatrzymaj etap w bezpiecznym miejscu,
  zaktualizuj dokumentację i przygotuj handover — nie kończ w połowie bez zapisu stanu.
```
