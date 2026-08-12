# EmberTern — audyt gotowości wydania i propozycja licencjonowania

**Data:** 2026-08-11  
**Zakres:** audyt statyczny repozytorium, konfiguracji publikacji, zależności, testów, bezpieczeństwa, UX oraz projekt systemu licencjonowania.  
**Zasada:** kod i artefakty repozytorium są źródłem prawdy; dokumentacja jest traktowana wyłącznie jako materiał pomocniczy.

## Executive Summary

**Werdykt:** EmberTern nie jest jeszcze gotowy do płatnej, publicznej publikacji.

Projekt ma dojrzałe fundamenty: separację `App → Firebird → Core`, świadomy model transakcji, osobne attachmenty Firebird dla danych/metadanych/DDL, ochronę ustawień DPAPI, mechanizm ochrony przed nadpisaniem obiektów oraz szeroką bazę testów. To jest dobra architektura do dalszego rozwoju i nie wymaga refaktoryzacji „dla czystości”.

Najważniejszy blocker wydania to brak wiarygodnej bramki jakości: Release build przechodzi z zerową liczbą ostrzeżeń, ale pełny suite testów kończy się wynikiem **8722/8768** — 46 testów nie przechodzi. Należy także zamknąć ryzyka związane z charsetami Firebird, potwierdzaniem nieodwracalnych skutków debuggera, logowaniem oraz dystrybucją binariów.

Architektura jest gotowa do dalszego rozwoju. Główny dług strukturalny to rosnąca odpowiedzialność `MainWindowViewModel`, nie wada samych granic projektów.

## Weryfikacja wykonana podczas audytu

| Obszar | Wynik |
|---|---|
| `dotnet build EmberTern.slnx -c Release --no-restore` | sukces, 0 ostrzeżeń, 0 błędów |
| `dotnet test EmberTern.slnx -c Release --no-build` | 8722 zaliczone, 46 niezaliczonych, 8768 łącznie |
| Testy `DiagnosticsLocalizationTests` uruchomione samodzielnie | sukces: 11/11 |
| Testy `ConnectionProfileStoreTests` uruchomione samodzielnie | 14/15; błąd testu DPAPI bez profilu użytkownika Windows |
| Skan NuGet vulnerabilities, także transitive | brak aktualnie zgłoszonych podatnych pakietów |
| Publikacja FolderProfile | sukces: self-contained `win-x64`, single-file, bez trimming |
| Podpis Authenticode wyniku publikacji | `EmberTern.exe` jest niepodpisany |

## Mocne strony

- Jednokierunkowe zależności projektów: `App → Firebird → Core`; Core nie zależy od Avalonia ani sterownika Firebird.
- Model transakcji jest wyraźny: brak autocommitu, ręczny commit/rollback, serializacja komend przez semafory i odrębne attachmenty.
- `ApplicationSettingsStore` ma migracje, atomowy zapis, backup i odmawia nadpisania nieczytelnego lub nowszego `settings.dat`.
- Kompilacja istniejącego obiektu przechodzi przez `ObjectChangeGate`: baza jest ponownie odczytywana, a zmieniona definicja lub zajęta nazwa blokuje zapis.
- Import korzysta z jednego pipeline’u dla Validate i Import, ma anulowanie, raportowanie oraz zewnętrzną ochronę przed nieobsłużonym wyjątkiem.
- Zapytania mają serwerowe anulowanie (`FbCommand.Cancel`), postęp dla dużych wyników oraz limit ochronny dla materializowanego widoku.
- Powiadomienia third-party są osadzone w aplikacji i dostarczane obok artefaktu publikacji.

## P0 — Blockers

### P0-01 — pełny suite testów nie jest zielony

**Lokalizacja:** `src/EmberTern.App/Localization/Loc.cs`, `src/EmberTern.App/ViewModels/MainWindowViewModel.cs`, testy lokalizacji.

**Mechanizm:** `Loc.LanguageChanged` jest zdarzeniem statycznym. `MainWindowViewModel` subskrybuje je, lecz nie zwalnia subskrypcji przez kontrolowany lifecycle. W pełnym przebiegu testów powoduje to dostarczanie powiadomień do obiektów Avalonia z niewłaściwego wątku.

**Scenariusz:** test zmienia katalog/kulturę lokalizacji po tym, jak wcześniejszy test pozostawił VM lub binding w statycznej liście subskrybentów. Avalonia odrzuca zmianę właściwości z obcego wątku.

**Ryzyko:** brak powtarzalnej certyfikacji release i możliwość ukrytych problemów lifetime/thread-affinity.

**Rekomendacja:**

1. Uporządkować ownership i odsubskrybowanie globalnych zdarzeń.
2. W testach izolować globalny stan `Loc` oraz wszystkie headless UI objects.
3. Ustalić testowy kontrakt dla DPAPI: test integracyjny wyłącznie na agencie z załadowanym profilem Windows albo jawne pominięcie w środowiskach bez profilu.
4. Zablokować release, dopóki pełny suite nie przejdzie powtarzalnie.

**Czy przed licencjonowaniem:** tak — to podstawowa bramka jakości dla dalszych zmian.

## P1 — High Priority

### P1-01 — debugger dopuszcza nieodwracalne skutki po zwykłym ostrzeżeniu

**Lokalizacja:** `src/EmberTern.App/Debugging/DebugPreflight.cs`.

Debugger wykrywa `IN AUTONOMOUS TRANSACTION`, `GEN_ID` i `NEXT VALUE FOR`, ale oznacza je jako ostrzeżenia nieblokujące. Rollback debug session nie cofnie autonomicznej transakcji ani zwiększonego generatora.

**Scenariusz:** użytkownik traktuje debugger jako bezpieczną symulację, uruchamia procedurę z generatorem lub autonomiczną transakcją, a stan bazy pozostaje zmieniony.

**Rekomendacja:** wymagaj jawnego potwierdzenia „Rozumiem nieodwracalne skutki” przed startem. Docelowo warto dodać tryb safe, który blokuje znane efekty uboczne.

**Czy przed licencjonowaniem:** tak; zalecane przed publicznym release.

### P1-02 — logi diagnostyczne są niezarządzane i mogą ujawniać kontekst klienta

**Lokalizacja:** `src/EmberTern.App/Program.cs`, `src/EmberTern.Firebird/FirebirdConnectionService.cs`.

Hasła są maskowane, co jest prawidłowe. Jednak `%TEMP%\\EmberTern-debug.log` przechowuje pełne wyjątki, nazwy profili, endpointy i ścieżki baz bez retencji, jasnej lokalizacji dla użytkownika i zasad redakcji.

**Rekomendacja:** osobny katalog logów, limit wielkości/rotacja, redakcja, wyraźny mechanizm „Open logs” oraz komunikat crash recovery dla użytkownika.

### P1-03 — ryzyko cichej utraty znaków przez charset Firebird wymaga zamknięcia

**Dowód:** `docs/current-state.md` opisuje zmierzony na Firebird 5 przypadek konwersji znaku spoza charsetu połączenia do `?`; domyślnym charsetem jest `WIN1250`.

Import ma ochronę charsetu. Nie potwierdzono natomiast podczas audytu, czy identyczny efekt występuje dla F5 i źródeł DDL przesyłanych przez sterownik.

**Rekomendacja:** wykonać reprodukcję na realnym Firebirdzie dla parametrów, F5 i DDL. Jeżeli ścieżka jest podatna, zastosować wspólny guard z `EncoderExceptionFallback`.

**Status:** wymaga weryfikacji live; nie należy zakładać, że problem dotyczy każdej ścieżki.

### P1-04 — publikacja działa, ale proces dystrybucji nie jest gotowy operacyjnie

**Lokalizacja:** `src/EmberTern.App/Properties/PublishProfiles/FolderProfile.pubxml`.

Profil publikuje aplikację jako self-contained `win-x64`, single-file, bez trimming. Test publikacji zakończył się powodzeniem. Artefakt ma jednak dodatkowe natywne pliki obok EXE i EXE nie ma podpisu Authenticode.

Brakuje powtarzalnego pipeline’u release, podpisu binariów, testu clean-install/upgrade, SBOM i polityki aktualizacji.

**Rekomendacja:** zamknąć te elementy przed instalatorem; podpis binariów zdecydowanie zalecany przed płatną dystrybucją.

### P1-05 — istotne ograniczenie UX na małych ekranach i wysokim DPI

**Dowód:** `docs/current-state.md` wskazuje, że command bary Activity Monitor i Data Import są poziomymi `StackPanel` bez `ScrollViewer` i są obcinane już przy 1366×768 oraz 150/175% DPI.

**Rekomendacja:** zapewnić responsive wrap/overflow/scroll przed pierwszym release.

### P1-06 — dokumentacja architektury jest sprzeczna z kodem

`ARCHITECTURE.md` opisuje m.in. Avalonia 12.0.3, cross-platform, `connections.json`, 281 testów i trzy runtime NuGety. Kod używa Avalonia 12.1.1, szyfruje ustawienia DPAPI, ma 8768 przypadków testowych i więcej zależności runtime.

**Rekomendacja:** przed release utworzyć krótki dokument „as built” generowany lub aktualizowany wraz z release. Nie używać starego dokumentu jako materiału sprzedażowego ani instrukcji wdrożenia.

## P2 — Medium

- `MainWindowViewModel` ma 8596 linii. Nie rozbijać mechanicznie; przy zmianach wydzielać stabilne koordynatory: lifecycle połączeń, workspace, routing komend i operacje kart.
- Brakuje automatycznej integracji z realnymi wersjami Firebird w solution/CI. Istnieją probe’y, ale nie stanowią stale wykonywanej bramki.
- `Avalonia.Controls.DataGrid` jest deprecated. Nie blokuje v1, lecz wymaga zaplanowanej migracji.
- PDB-y są publikowane wraz z artefaktem; należy świadomie ustalić politykę symboli dla kanału produkcyjnego.
- Pełna lokalizacja i dalsze UX polishing mogą zostać po pierwszym wydaniu, jeśli podstawowe przepływy są testowane ręcznie.

## P3 — Low

- Kilka pakietów ma nowsze wersje, ale aktualny skan nie zgłasza podatności. Nie aktualizować ich mechanicznie tuż przed release.
- Dodatkowe funkcje: updater, command palette, telemetry opt-in i pełniejsza konfiguracja mogą poczekać.

## Audyt funkcjonalny i architektoniczny

### Potwierdzone mechanizmy

| Obszar | Stan potwierdzony w kodzie |
|---|---|
| SQL editor | wykonywanie F5/Shift+F5, parametry, cancel serwerowy, preview/full limit, wynik, eksport i komunikaty |
| Transakcje | jedna robocza transakcja data lane, ręczny commit/rollback, status oraz liczenie poleceń |
| Firebird | data/metadata/DDL lanes, osobne locki i degraded mode przy nieudanym dodatkowym attachmencie |
| Metadane | sidebar, wyszukiwanie, DDL, edytory obiektów, eksport metadata i PortableDDL |
| Ochrona zmian | fingerprint definicji, ponowny odczyt przed compile, blokada stale overwrite i kolizji nazw |
| Data Import | Validate/Import przez ten sam pipeline, transakcja importu, anulowanie, raport oraz obsługa nowej tabeli |
| Language tooling | lexer, parser, formatter, semantyka, completion, diagnostics, quick fixes i debugger |
| Settings | jeden `settings.dat`, DPAPI, atomic write, lock między procesami, migracje oraz bezpieczna odmowa nadpisania |
| Diagnostyka | Activity Monitor, Session Manager, trace, performance panel i debug log |

### Stabilność i lifecycle

Model połączeń i transakcji jest starannie zaprojektowany. W szczególności serializacja komend na pojedynczym `FbConnection` oraz rozdzielenie attachmentów jest właściwą odpowiedzią na ograniczenia FirebirdClient.

Największe ryzyko stabilności leży w rosnącym orchestration layer i globalnych zdarzeniach, nie w bazowym modelu Firebird. Każda nowa funkcja powinna mieć zdefiniowanego właściciela anulowania, disposal i odsubskrybowania eventów.

### Testy

Testów jest dużo i obejmują parser, formatter, semantykę, UI headless, transakcje, import, export, settings i kontrakty UX. To silna baza regresyjna.

Nie zastępuje ona jednak testów integracyjnych z realnym Firebirdem. Przed wydaniem należy ręcznie lub automatycznie zweryfikować co najmniej: uprawnienia ograniczonego użytkownika, limit attachmentów, rozłączenie podczas pracy, anulowanie długiej operacji, duże wyniki, charset oraz DDL na realnej bazie.

## Bezpieczeństwo

- Profile i hasła są szyfrowane DPAPI CurrentUser. To odpowiedni domyślny mechanizm dla docelowego Windowsowego produktu.
- Kopia `settings.dat` na inny komputer/użytkownika nie jest automatycznie nadpisywana, co chroni przed utratą danych.
- Parametry proceduralne są wiązane, a generatorzy SQL stosują quoting/literal escaping. Surowe SQL w konsoli pozostaje świadomą funkcją narzędzia deweloperskiego.
- Nie znaleziono implementacji Firebase ani licencjonowania w produkcyjnym kodzie.
- Przed release należy dodać secret scan i SCA/SBOM do CI.

## Licencjonowanie

### Ocena koncepcji

Model offline-first jest właściwy dla EmberTern. Lokalna, ważna licencja powinna umożliwiać normalną pracę bez Internetu. Sieć powinna być potrzebna wyłącznie przy aktywacji, odnowieniu, zmianie seatów lub dobrowolnym odświeżeniu.

Trzeba zaakceptować ograniczenie: **bez wiązania z urządzeniem nie da się technicznie uniemożliwić skopiowania licencji.** Można ograniczać nadużycia rejestrem aktywacji, liczbą seatów, umową i audytem, ale nie można zapewnić twardej ochrony przed kopiowaniem pliku.

### Rekomendowany model

Podpisana kryptograficznie licencja offline + opcjonalny backend aktywacyjny.

- Aplikacja zawiera tylko klucz publiczny Ed25519.
- Prywatny klucz podpisujący nie trafia do aplikacji, Firebase ani dystrybuowanego License Managera.
- Aplikacja lokalnie sprawdza podpis, produkt, format, daty i entitlementy.
- Firebase/backend pełni rolę rejestru aktywacji, odnowień, blokad i historii, a nie wymaganego serwera dla zwykłego uruchomienia.
- Licencja czasowa ma `expiresAt`; opcjonalny managed-offline ma `offlineAllowedUntil`.

Nie można równocześnie zagwarantować nieograniczonego offline i natychmiastowego cofnięcia licencji. Wybór biznesowy:

| Polityka | Offline | Cofnięcie licencji |
|---|---|---|
| Strict offline | nieograniczony | dopiero przy kolejnym kontakcie z serwerem |
| Managed offline | np. 90 dni | po zakończeniu podpisanego lease |

### Firebase

Firebase może być rejestrem, ale desktop client nie powinien bezpośrednio czytać ani modyfikować licencji w Firestore. Klucz licencyjny nie jest tożsamością i nie stanowi bezpiecznej podstawy reguł dostępu.

Wymagany jest backendowy endpoint aktywacji z limitowaniem żądań, atomowym przydziałem seatów i audytem. Biblioteki serwerowe Firestore omijają Security Rules, więc backend musi mieć ograniczone IAM.

Aktualne limity bezpłatnego Firestore są wystarczające dla małej bazy licencji: 50 000 odczytów i 20 000 zapisów dziennie. Cloud Functions wymagają jednak planu Blaze, dlatego „wyłącznie darmowy Firebase” nie jest trwałą gwarancją dla bezpiecznej aktywacji.

Źródła:

- [Firebase pricing plans](https://firebase.google.com/docs/projects/billing/firebase-pricing-plans)
- [Cloud Firestore pricing and free quota](https://firebase.google.com/docs/firestore/pricing)
- [Firestore security overview](https://firebase.google.com/docs/firestore/security/overview)

### Alternatywy

| Model | Bezpieczeństwo | Offline | Koszt/utrzymanie | Ocena |
|---|---|---:|---:|---|
| Klucz + bezpośredni odczyt Firebase z desktopu | słabe | dobre | pozornie niski | odrzucić |
| Podpisana licencja + backend aktywacji | dobre proporcjonalnie | bardzo dobre | umiarkowane | rekomendowany |
| Ręcznie wydawany podpisany plik licencji | dobre kryptograficznie | pełne | niskie, mniej wygodne | dobry MVP i offline activation |

### Format techniczny

Przykładowy format:

```text
ETL1.<payload-base64url>.<signature-base64url>
```

Podpis obejmuje dokładne bajty payloadu. Nie należy podpisywać swobodnie serializowanego JSON bez kanonicznej reprezentacji.

Payload powinien zawierać:

- `formatVersion`, `keyId`, `licenseId`;
- `product`, `edition`, `features`;
- `customerId` i wyświetlaną nazwę klienta;
- `issuedAt`, `notBefore`, `expiresAt`, opcjonalnie `offlineAllowedUntil`;
- `seatPolicy`, `maxInstallations`;
- `activationId`, opcjonalny losowy `installationId`;
- `issuer`, `termsVersion`.

Lokalnie nie przechowywać prywatnego klucza ani klucza aktywacyjnego. Sam podpisany plik licencji nie musi być szyfrowany; jego integralność zapewnia podpis. Pomocniczy stan instalacji i wykrywanie cofania zegara można trzymać oddzielnie, chronione DPAPI.

### Aktywacja

1. Użytkownik podaje klucz.
2. Aplikacja wysyła klucz, losowy `installationId` i wersję aplikacji.
3. Backend sprawdza status i limity seatów.
4. Backend wydaje podpisany entitlement.
5. Aplikacja waliduje podpis lokalnie i zapisuje licencję.

### Offline activation

1. Aplikacja generuje request z kluczem i losowym `installationId`.
2. Administrator importuje request do bezpiecznego narzędzia.
3. Narzędzie tworzy podpisaną odpowiedź.
4. Użytkownik importuje odpowiedź, a aplikacja waliduje ją identycznie jak licencję online.

Ukryty ekran nie jest zabezpieczeniem; zabezpieczeniem jest podpis oraz ochrona prywatnego klucza.

## License Manager

Avalonia License Manager może być administracyjnym UI, ale nie może zawierać Firebase service-account ani prywatnego klucza podpisującego.

Funkcje:

- generowanie kluczy aktywacyjnych;
- klienci, licencje, daty, statusy, seat policy i entitlementy;
- odnowienie, blokada oraz cofnięcie;
- wyszukiwanie i filtrowanie;
- append-only audit: kto, kiedy, co zmienił, wartości przed/po;
- eksport CSV;
- import/export offline activation requests.

Manager powinien komunikować się z backendem administracyjnym. Prywatny klucz należy przechowywać w sekretach backendu lub wydzielonym, operatorowym narzędziu offline.

## Prosty model biznesowy

Na start wystarczą trzy typy:

1. **Trial** — 14 lub 30 dni.
2. **Developer annual** — jedna nazwana osoba, pełny zestaw funkcji.
3. **Company annual** — N nazwanych seatów, wspólne zarządzanie.

Nie wdrażać jeszcze floating, workstation ani perpetual. Floating wymaga stałej dostępności usługi, a perpetual komplikuje upgrade i support. Każdy z tych modeli można później dodać jako nowe entitlementy bez zmiany podpisu ani formatu.

## Release Readiness — co zamknąć przed instalatorem

1. Zielony, powtarzalny pełny test suite.
2. Wynik live testów Firebird dla charsetu, rozłączeń, anulowania, uprawnień i limitów attachmentów.
3. Potwierdzenie ryzyka debuggera dla efektów nieodwracalnych.
4. Polityka logów, crash recovery i redakcji danych.
5. Wersjonowanie release, podpis Authenticode, SBOM i pipeline publikacji.
6. Ustalona lokalizacja danych użytkownika, licencji i logów.
7. Migracja ustawień/licencji oraz scenariusze reinstall, upgrade i downgrade.
8. Uninstall: zachować dane użytkownika domyślnie i wyraźnie pytać o ich usunięcie.
9. Test czystej instalacji oraz upgrade istniejącej instalacji.
10. EULA, polityka prywatności i kanał wsparcia.

## Recommended Roadmap

1. Naprawić P0: zazielenić i ustabilizować pełny suite testów.
2. Zamknąć P1: charset, debugger acknowledgement, logi i dostępność UI na DPI.
3. Zamrozić zakres EmberTern 0.5.x i zaktualizować dokumentację „as built”.
4. Zaprojektować izolowany moduł `EmberTern.Licensing`: podpis, validator i entitlement policy.
5. Zbudować backend aktywacji oraz License Manager bez sekretów po stronie klienta.
6. Dodać offline activation jako ten sam podpisany format.
7. Dodać signing, SBOM, clean-install/upgrade tests i instalator.
8. Wykonać końcowe testy na realnym Firebirdzie i przygotować release.

## Stan audytu

Nie wprowadzono zmian w kodzie źródłowym ani konfiguracji produktu. Raport został przygotowany na podstawie aktualnego kodu, artefaktów build/publish i wykonanych testów.
