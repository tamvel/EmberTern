# EmberTern — słownik terminologii UI

> **🔒 Ratyfikowany przez użytkownika 2026-08-10 (Product Polish M5 / M‑4).**
> Ten dokument jest **normą**, nie propozycją. Egzekwuje go
> `TerminologyTests` (`tests/EmberTern.Tests/TerminologyTests.cs`) — ryzyko **R‑8** z `product-polish.md`.
>
> ⛔ **Dodając nową akcję do UI, weź czasownik STĄD.** Jeżeli żaden nie pasuje, to jest sygnał, że operacja
> jest inna niż myślisz — zatrzymaj się i rozstrzygnij, zamiast wprowadzać czasownik spoza słownika.

---

## §1 Czasowniki akcji

| czasownik | znaczenie | przykłady |
|---|---|---|
| **Drop** | ⭐ operacja **DDL generująca `DROP` w bazie** | `DROP TABLE` · `DROP INDEX` · `DROP ROLE` · `DROP USER` · `ALTER TABLE … DROP <pole>` · `ALTER TABLE … DROP CONSTRAINT` |
| **Delete** | usunięcie trwałego elementu, które **nie jest** operacją `DROP` | profil połączenia · folder · zapisane zapytanie · profil importu · **wiersz danych** (`DELETE FROM`) |
| **Remove** | usunięcie elementu z **edytowanej kolekcji** — bufor, nie baza | warunek filtra · linia agregacji · kolumna widoku w trybie Easy · parametr procedury · zmienna |
| **Clear** | opróżnienie **pojemnika**, nie usunięcie rzeczy | Clear editor · Clear messages · Clear all saved queries |
| **Discard** | odrzucenie **niezapisanych zmian** | „Discard and close" · „Discard and exit" |
| **New** | nowy **obiekt lub dokument** | New table · New connection · New folder · New saved query |
| **Add** | dodanie **elementu do istniejącej kolekcji** | Add field · Add condition · Add aggregate · Add parameter · Add row |
| **Execute** | wykonanie **SQL, procedury lub komendy** | Execute · Execute query · Execute procedure · Execute (skrypt) |
| **Compile** | zapis **definicji obiektu** do bazy | Compile (edytory obiektów) |
| **Save** | zapis **pliku, profilu lub opisu** | Save script · Save as… (profil importu) · Save (opis obiektu) |
| **Issue** | ⭐ **podpisanie kluczem prywatnym i zapisanie artefaktu licencji** do rejestru | Issue and save… · Extend and issue · Export this issue… *(License Manager)* |
| **Rollback** | wycofanie transakcji — ⚠ **zawsze jedno słowo** | Rollback · Rollback transaction |

### §1.1 ⭐⭐ Dlaczego `Drop` NIE zostało sprowadzone do `Delete`

🔒 **Decyzja użytkownika:** *„EmberTern jest narzędziem dla developerów baz danych, więc chcę zachować
informację o tym, jaka operacja DDL zostanie wykonana. Nie sprowadzaj Drop do Delete tylko dla jednolitości
języka."*

⭐ Rozróżnienie **niesie informację**, a nie tylko styl: użytkownik czytający „Drop index" wie, że wykona się
`DROP INDEX`, a czytający „Delete connection" wie, że baza nie zostanie ruszona. ⛔ Ujednolicenie do jednego
czasownika **skasowałoby tę informację** — i to jest cena, której ten produkt nie płaci.

⚠ **Reguła istniała w produkcie utajona i była łamana mniej więcej w połowie przypadków.** M‑4 nie wprowadziło
jej z zewnątrz — **dokończyło ją**.

### §1.1a ⭐⭐ Dlaczego `Issue` NIE zostało sprowadzone do `Save` ani `Compile`

🔒 **Decyzja użytkownika (2026-08-20, L8.0/prep).** `Issue` opisuje operację, której żaden istniejący
czasownik nie obejmuje: **podpisanie danych kluczem prywatnym** i zapisanie powstałego artefaktu do
append-only kolumny rejestru. `Compile` znaczy „zapis definicji obiektu **do bazy Firebirda**", `Save`
„zapis pliku, profilu lub opisu" — a tu nie chodzi ani o obiekt bazy, ani o plik: chodzi o **wytworzenie
dowodu**, którego nie da się cofnąć ani poprawić.

⭐ To jest dokładnie sytuacja, o której mówi ostrzeżenie na górze §1 — *„jeżeli żaden czasownik nie pasuje,
operacja jest inna, niż myślisz"*. Słownik został **rozszerzony**, a nie nagięty.

⚠ Zasięg: **wyłącznie License Manager.** Produkt nie wystawia licencji i nie ma powierzchni, na której ten
czasownik mógłby się pojawić.

### §1.2 `Run` — dozwolone wyłącznie w innym znaczeniu

⛔ `Run` **nie jest** synonimem `Execute`. Wolno go użyć tylko tam, gdzie znaczy co innego niż „wykonaj to":
**„Run to cursor"** w debuggerze (biegnij do punktu), oraz jako **stan** („Running…", „Not run") — bo to
imiesłów opisujący przebieg, a nie nazwa akcji.

### §1.3 Wielkość liter

| co | zapis | przykład |
|---|---|---|
| **etykieta akcji** (przycisk, pozycja menu, tooltip) | **zdaniowa** | „Delete connection", „Add field", „Drop index" |
| **nazwa własna** funkcji, obszaru, okna | **Title Case** | „Activity Monitor", „Data Import", „Developer Mode", „About EmberTern" |

### §1.4 `CommandTitle*` — rejestr opisowy, nie kopia etykiety

🔒 Ratyfikowane: nazwy w katalogu komend (widoczne w oknie **Keyboard Shortcuts**) **mogą być bardziej
opisowe** niż etykieta na ekranie — „Execute query, all rows" jest tam lepsze niż samo „Execute". ⛔ **Ale
muszą korzystać z tego samego słownika** i **nie wolno im wprowadzać sprzecznego czasownika dla tej samej
operacji**: jeżeli UI mówi „Remove condition", katalog nie może mówić „Delete selected item".

---

## §2 Nazwane wyjątki — świadome, z powodem

⛔ **To NIE są niedoróbki.** Każdy ma zapisany powód; nie „poprawiać" ich przy okazji innego etapu.

### §2.1 Debugger — „Save", choć operacja kompiluje

`DebuggerSave = "Save"` (przycisk) i `CommandTitleDebuggerSaveSource = "Save debugged source"`.

⚠ **Zmierzone:** ta akcja przechodzi przez `ObjectChangeGate`, odmawia komunikatem `EditorNothingToCompile`,
jej tooltip brzmi *„Save and compile the routine"*, a potwierdzenie *„Saving recompiles {0}"* — czyli
**kompiluje definicję obiektu do bazy**. Wedle §1 byłoby to `Compile`.

🔒 **Decyzja użytkownika: zostaje `Save`.** *„Semantyka tej akcji to zapisanie zmian źródła i ich kompilacja
do bazy, więc «Save» opisuje akcję użytkownika, podczas gdy «Compile» opisuje operację techniczną."*
⭐ Wzmacnia to struktura: w debuggerze „Save" jest **odpowiednikiem „Discard"** w bramce niezapisanej pracy,
a wszystkie edytory obiektów mają tę samą dwoistość (przycisk „Compile", dialog „Save and close").
⛔ Nie zmieniać bez osobnej decyzji.

### §2.2 `Delete rule` — rzeczownik, nie akcja

`TableDetailConstraintDeleteRule = "Delete rule"` to **nazwa kolumny** opisującej regułę `ON DELETE` klucza
obcego. ⛔ Nie podlega §1 — to nie jest przycisk.

### §2.3 Stan a akcja

`ScriptResultNotRun = "Not run"` i `ScriptStatusRunning = "Running…"` opisują **stan przebiegu**, nie akcję.
⛔ Nie zamieniać na „Not executed" / „Executing…" — §1.2.

### §2.4 „Roll back" w zdaniu

⚠ `Rollback` jest jednym słowem jako **nazwa operacji i etykieta**. W zdaniu, gdzie występuje jako
**czasownik frazowy**, poprawną angielszczyzną jest „roll back" — i tak zostaje
(np. *„Transaction OPEN — commit or roll back."*). ⛔ Nie przepisywać zdań na siłę.

---

## §3 Miejsca semantycznie niepewne — ⏸ NIEZMIENIONE, czekają na decyzję

⚠ Zapisane, żeby nie wypadły między etapami (#340). ⛔ Nie zmieniać bez wskazania użytkownika.

| miejsce | stan | dlaczego nie ruszone |
|---|---|---|
| `FolderDialogCreate = "Create"` | przycisk potwierdzenia w dialogu „New folder" | tytuł dialogu nazywa **rzecz** („New folder"), a przycisk **czynność** — to jest poprawny wzorzec UI, nie kolizja. ⚠ Ale §1 nie ma dla niego reguły, więc pozostaje otwarte |
| `DialogNewConnectionTitle` · `DialogEditConnectionTitle` · `BlobEditorTitle` | **Title Case** | to **tytuły okien**, a §1.3 rozstrzyga tylko etykiety akcji i nazwy własne. Tytuł okna jest trzecią kategorią |
| `*TabDefaultTitle` („New Table", „New View", …) | **Title Case** | to **nazwy dokumentów** w pasku zakładek, a nie akcje |
| `ScriptStatusNothingToRun` · `ScriptStatusDisallowedFormat` | zawierają „run" w zdaniu | proza, nie etykieta — §1.2 dotyczy czasownika akcji. ⚠ Granicę wyznacza strażnik długością (≤ 24 znaki) |
| `TableDetailConstraintDeleteRule = "Delete rule"` | rzeczownik | §2.2 |
| `DebuggerSave` · `CommandTitleDebuggerSaveSource` | „Save", choć kompiluje | §2.1 — nazwany wyjątek, decyzja użytkownika |

### §3.1 ⚠ Znalezisko o samym pomiarze, warte zapamiętania

Pierwszy inwentarz M‑4 raportował, że `Drop` występuje **wyłącznie w Data Import**. Zmierzone potem:
**26 etykiet w sześciu modułach**, z czego Data Import to **2** — niecałe 8 %. Błąd wziął się z podglądu
uciętego na `head -45`, który nie doszedł do wpisów Table / Index / Security, i **decyzja projektowa
została na nim oparta**, zanim pomiar ją obalił.

⭐ To ten sam kształt co #335 i §19.47.7: **uogólnienie z tego, co się akurat wydrukowało.** Praktyczny
wniosek: przy inwentaryzacji słownictwa **licz wystąpienia per moduł**, zanim nazwiesz coś wyjątkiem —
„wyjątek" jest twierdzeniem o rozkładzie, a nie o pojedynczym napisie.

---

## §4 License Manager — słownik EN → PL

> **🔒 Ratyfikowany przez użytkownika 2026-08-20 (L8.0/prep), jako BAZA etapu L8.**
> ⚠⚠ **Baza, nie zamknięcie.** Użytkownik zastrzegł wprost: *„jeśli podczas L8.6 okaże się, że konkretny
> termin źle brzmi w realnym UI, możemy go jeszcze skorygować"*. ⛔ Korekta wymaga jego decyzji i wpisu
> tutaj — ⛔ nie wolno jej zrobić po cichu przy tłumaczeniu.
>
> ⚠ Zasięg: **License Manager**. `TerminologyTests` żyje w `EmberTern.Tests`, czyli w innym rozwiązaniu, i
> tej sekcji dziś nie widzi — strażnik po stronie Managera jest pozycją L8.5.

### §4.1 ⛔⛔ Odziedziczone z produktu — NIE do wymyślania na nowo

⭐⭐ **Zmierzone, nie wybrane.** EmberTern ma już ratyfikowaną polszczyznę dla niemal całego słownictwa
licencyjnego (`src/EmberTern.App/Localization/Strings.pl.resx`), a License Manager opisuje **te same fakty
o tych samych licencjach**. ⛔ Każde inne słowo tutaj znaczyłoby, że ten sam fakt nazywa się inaczej po
stronie wystawcy i po stronie klienta.

| EN | PL | klucz w produkcie |
|---|---|---|
| Licensed to | **Licencjobiorca** | `SettingsLicenseLicenseeLabel` |
| Licence id | **Identyfikator licencji** | `SettingsLicenseIdLabel` |
| Seats | ⚠ **Stanowiska** *(⛔ nie „Miejsca")* | `SettingsLicenseSeatsLabel` |
| Valid from | **Ważna od** | `SettingsLicenseValidFromLabel` |
| Valid until (inclusive) | **Ważna do (włącznie)** | `SettingsLicenseValidUntilLabel` |
| Licence status | **Stan licencji** | `SettingsLicenseStatusLabel` |
| EmberTern licence *(typ pliku)* | **Licencja EmberTern** | `LicenseActivationFileTypeName` |
| Update licence… | **Aktualizuj licencję…** | `SettingsLicenseUpdateButton` |
| Save · Cancel · Close · Clear · Export · Settings · Language · Password | **Zapisz · Anuluj · Zamknij · Wyczyść · Eksportuj · Ustawienia · Język · Hasło** | katalog produktu |

⭐⭐ **`VerdictText` — pięć werdyktów musi brzmieć DOKŁADNIE tak, jak powie EmberTern klientowi.** Ta kolumna
odpowiada na pytanie *„co EmberTern powiedziałby o tym dzisiaj"*, więc każde inne słowo jest tam nieprawdą.

| `LicenseStatus` | PL | klucz w produkcie |
|---|---|---|
| `Valid` | **Licencja aktywna** | `LicenseStatusValid` |
| `Grace` | **Licencja wygasła — okres karencji** | `LicenseStatusGrace` |
| `Expired` | **Licencja wygasła** | `LicenseStatusExpired` |
| `NotYetValid` | **Licencja jeszcze nieaktywna** | `LicenseStatusNotYetValid` |
| *(odmowa)* | **Nie można odczytać licencji** | `LicenseStatusInvalid` |

### §4.2 Czasowniki akcji — nad słownikiem §1

| EN | PL |
|---|---|
| Save customer · Save terms | **Zapisz klienta · Zapisz warunki** |
| New · New licence | **Nowy · Nowa licencja** |
| Issue and save… | **Wystaw i zapisz…** *(§1 `Issue`)* |
| Extend and issue | **Przedłuż i wystaw** |
| Export this issue… · Export latest… | **Eksportuj to wydanie… · Eksportuj najnowsze…** |
| Send licence… · Send · Send test email… | **Wyślij licencję… · Wyślij · Wyślij wiadomość testową…** |
| Inspect latest | **Sprawdź najnowsze** |
| Backup… | 🔒 **Utwórz kopię zapasową…** *(D‑2)* |
| Restore… · Restore | **Przywróć… · Przywróć** |
| Revert | **Przywróć zapisane** — ⛔ nie „Cofnij": to nie undo, to ponowny odczyt pliku |
| Forget settings | 🔒 **Usuń zapisane ustawienia** *(D‑3)* — §1 `Delete`, nie idiom „zapomnij" |
| Clear selection · Select all shown | **Wyczyść zaznaczenie · Zaznacz wszystkie widoczne** |
| Unlock · Create signing key | **Odblokuj · Utwórz klucz podpisujący** |
| Open data folder | **Otwórz folder danych** |

### §4.3 Rzeczowniki własne License Managera

| EN | PL |
|---|---|
| Customer · Customers | **Klient · Klienci** |
| Licence · Licences | **Licencja · Licencje** |
| Identifier | **Identyfikator** |
| Contact | **Kontakt** |
| Notes | **Notatki** |
| Product | **Produkt** |
| Expiry *(kolumna)* | **Wygasa** |
| Status *(kolumna)* | **Stan** |
| Standing *(kolumna)* | 🔒 **Termin** *(D‑4)* |
| Issuing · Issuing history | **Wystawianie · Historia wystawień** |
| Artifact | 🔒 **Artefakt** *(D‑5)* |
| Reason for this issue | **Powód wystawienia** |
| Initial issue · Renewal · Terms change · Re-issue — lost file | **Pierwsze wystawienie · Odnowienie · Zmiana warunków · Ponowne wystawienie — utracony plik** |
| current · superseded *(prezentacja)* | **bieżący · zastąpiony** |
| Register of record | **Rejestr wzorcowy** |
| Encrypted backup | **Zaszyfrowana kopia zapasowa** |
| Signing keystore | **Magazyn kluczy podpisujących** |
| Passphrase | 🔒 **Hasło dostępu** *(D‑6)* |
| Signing key *(zakładka)* | **Klucz podpisujący** — ⭐ zgodne z ratyfikowanym „Utwórz klucz podpisujący" (§4.2) |
| Key id | **Identyfikator klucza** — ⭐ jak `Identifier` / `Licence id` powyżej, nie „ID klucza" |
| Public key fingerprint | **Odcisk klucza publicznego** |
| Verify backup… | **Sprawdź kopię zapasową…** — ⭐ `Verify` = **Sprawdź**, jak ratyfikowane „Inspect latest / Sprawdź najnowsze" (§4.2); ⛔ nie „Weryfikuj" |
| Copy | **Kopiuj** |
| Sender · Sign-in · Transport security | **Nadawca · Logowanie · Zabezpieczenie połączenia** |
| Message language · Application language | **Język wiadomości · Język aplikacji** |
| Attached | **Załącznik** |

⚠ **Siedem ostatnich wierszy powyżej dodał etap L7.1** (powierzchnia ceremonii w oknie Storage). ⛔ To
**rozszerzenie**, nie korekta — żaden ratyfikowany termin nie został zmieniony; każdy nowy dobrany tak, by
czytał się jak wyraz już zatwierdzony w §4.2/§4.3 (`Verify` → **Sprawdź** jak w „Inspect latest", `Key id`
→ **Identyfikator klucza** jak w „Licence id"). ⚠ Wartości techniczne w tych etykietach — `SPKI`, `base64`,
`SHA-256`, `TrustedKeys.Production` — zostają nieprzetłumaczone na mocy §4.4.

⭐ **`Passphrase` ≠ `Password`, i rozróżnienia nie wolno zgubić** (D‑6). Aplikacja rozróżnia je celowo: hasło
skrzynki SMTP to `Password` / **Hasło**, sekret magazynu kluczy i kopii zapasowej to `Passphrase` /
**Hasło dostępu** — a komunikat o nim mówi *„sześć wygenerowanych słów"*, co nie jest tym samym rodzajem
rzeczy co hasło do poczty.

### §4.4 ⛔⛔ Czego NIE tłumaczymy — kontrakt techniczny

⚠ Sprawdzone w kodzie, nie założone. Lokalizacja nie może dotknąć ani jednej z tych wartości.

- **wartości persystowane**: `IssueReasons.*` (`initial`, `renewal`, `terms-change`, `reissue-lost`),
  `LicenseStatuses.*` (`active`, `blocked`), `RegisterQueries.Current` / `.Superseded`, typy JSONL
  (`customer`, `license`, `artifact`, `current-artifact`, `audit`);
- **akcje audytu**: `licence.sent`, `licence.send-failed`, `licence.exported`, `register.backed-up`,
  `register.exported`;
- ⭐⭐ **notatki audytu** — `LicenceDelivery` zapisuje angielskie zdania **do rejestru**. ⛔ Zostają
  angielskie i invariantne: log audytu, którego język zależy od tego, kiedy powstał wiersz, przestaje być
  jednym dokumentem;
- **nazwy plików i rozszerzenia**: `EmberTern.etlic`, `.etlmbak`, `.jsonl`, `.eml`, `licenses.db`,
  `keystore.etkeys`, `smtp.dat`;
- ⭐ **daty** — ISO `yyyy-MM-dd`, invariantnie, w 7 miejscach. Ta decyzja zostaje, a `DatePresentationTests`
  jej pilnuje;
- **branding**: `EmberTern`, `EmberTern License Manager`;
- ⭐ **nazwy języków w pickerze** — `English` / `Polski`, każdy nazwany W SOBIE. Jedyna osoba, która nie
  potrafi przeczytać bieżącego języka interfejsu, to dokładnie ta, która sięga po ten picker.
