# Runbook — ceremonia produkcyjnego klucza podpisującego

> **Status: ✅ WYKONANY 2026-08-22 (L7.3), klucz publiczny wysłany w L7.4.** Klucz `R1` istnieje,
> `TrustedKeys.Production` go niesie, a §35.4 jest wypełnionym rejestrem ceremonii.
>
> ⭐⭐ **Dokument zostaje jako REFERENCJA, nie jako historia.** Ta sama procedura obowiązuje przy
> **rotacji** klucza (§15.3) i przy odtworzeniu magazynu na innej maszynie — wtedy `R2` dostaje w §35.4
> **własny wiersz**, a ten nie jest edytowany. ⚠ Czytaj kroki w czasie teraźniejszym; opisują to, co
> należy zrobić, a nie to, co zrobiono raz.
>
> ⚠⚠ **Świadome ograniczenie PIERWSZEJ ceremonii, ratyfikowane przez użytkownika:** obie kopie offline
> zostały wykonane i sprawdzone przez `VerifyRestore` **na tej samej maszynie**; ⛔ kroku 6 „odtworzenie na
> DRUGIM komputerze" nie wykonano. Przenośność klucza pozostaje do potwierdzenia przy pierwszej
> rzeczywistej migracji, przez backup → restore → porównanie odcisku. ⭐ To była decyzja, nie pominięcie —
> ta maszyna jest docelowym środowiskiem wystawiania (§35.4).
>
> **Autorytet merytoryczny:** [`licensing-system.md`](licensing-system.md) §24 (zarządzanie kluczami),
> §15.2–§15.3 (`kid`, rotacja), §16.1 (`PrivateKeyNeverShipsTests`), §25 (model zagrożeń), §29 (odtwarzanie),
> §30 (ryzyka), §35.4 (rejestr ceremonii). ⚠ Ten runbook **nie zmienia** żadnej z tych decyzji — rozpisuje je
> na kroki.
>
> **Decyzje użytkownika ratyfikowane 2026-08-22, przed napisaniem tego dokumentu:**
> **A3** — ceremonia w świadomie oddzielonym, czystym środowisku. ⚠ **Doprecyzowane tego samego dnia przez
> użytkownika:** tą maszyną jest obecna, a devowe `keystore.etkeys` i `licenses.db` uznano za artefakty
> testowe QA i usunięto je przed ceremonią — nie zakładano osobnego konta ani maszyny ·
> **B1** — wykonawca ceremonii to minimalna sekcja w istniejącym oknie Storage (dostarczone w L7.1) · **C2** — ostrzeżenie o cofnięciu zegara odłożone jako
> świadomy backlog · **D1** — §35.4 **jest** rejestrem ceremonii, ⛔ bez osobnego „Appendix A".

---

## 0. Zasady obowiązujące przez całą ceremonię

⭐⭐ **Jedyny nieodwracalny zasób w całym systemie to klucz prywatny** (§25.1). Rejestr da się odtworzyć z
kopii, skrzynkę SMTP przekonfigurować, artefakty wystawić ponownie. Utracone hasło dostępu do magazynu
kluczy oznacza, że **nic już nigdy nie zostanie wystawione ani odnowione tym kluczem** (§29).

| | |
|---|---|
| **Sekret nr 1** | plik `keystore.etkeys` — klucz prywatny zaszyfrowany AES-256-GCM pod PBKDF2-SHA256, 600 000 iteracji, 32-bajtowa sól na plik |
| **Sekret nr 2** | **hasło dostępu** do tego pliku — jedyna rzecz między osobą, która ma plik, a możliwością bicia licencji |
| **Publiczne** | `kid`, klucz publiczny (SPKI, base64), odcisk SHA-256, wygenerowany wpis `TrustedKey` |

⛔ **Czego nie wolno nigdy i nigdzie:** hasła dostępu ani zawartości `keystore.etkeys` w repozytorium, w
`%TEMP%\EmberTern-debug.log`, w tym czacie, w treści commita, w zrzucie ekranu, w folderze
synchronizowanym z chmurą, w CI, w menedżerze haseł **wspólnym z zespołem**, w kopii niezaszyfrowanej
(§24.2). ⛔ Klucz prywatny nie jest częścią żadnej kopii zapasowej rejestru — `BackupWorkflow` go nie
dotyka i to jest zaprojektowane (§12.3).

⭐ **Asystent (Claude) nie widzi i nie zapisuje żadnego sekretu.** Hasło dostępu wpisujesz Ty, na maszynie
ceremonii. Do tej sesji wracają wyłącznie wartości publiczne: `kid`, odcisk, klucz publiczny.

**Czas i osoby.** Kroki 1–9 to jedno posiedzenie (~60–90 min), krok 6 wymaga **drugiego komputera**.
Kroki 10–14 to praca w repozytorium i mogą nastąpić później tego samego dnia.

**Warunek wyjścia całej ceremonii:** prawdziwa licencja widziana jako **`Valid` w buildzie `Release`** —
ta jedna linia kryterium §32 dla L4, którą §38.6 świadomie odłożył do L7, bo bez prawdziwego klucza nie
mogła być prawdziwa.

---

## Krok 1 — Przygotowanie czystego środowiska

**Co wykonujemy.** Odizolowane środowisko, w którym **nie istnieje** żaden devowy magazyn kluczy, żeby
pomyłka „dev vs produkcja" była **niemożliwa**, nie tylko nieprawdopodobna (decyzja A3).

⚠⚠ **Dlaczego to nie jest formalność.** `ManagerPaths.Default` jest zaszyty na
`%APPDATA%\EmberTern License Manager`. Jeśli leży tam jakikolwiek `keystore.etkeys`,
`SigningSession.Create` **odmówi** utworzenia drugiego — celowo, bo nadpisanie magazynu jest
nieodwracalne. Aplikacja nie ma przełącznika katalogu, a przekierowanie zmiennej `APPDATA` **nie działa**
(zmierzone 2026-08-22: .NET czyta ścieżkę z Win32 known-folder API), więc izolację daje **profil systemowy
albo maszyna**, nie opcja w UI.
⏭ *Przy pierwszej ceremonii w tej lokalizacji leżał magazyn deweloperski z QA; użytkownik uznał go za
artefakt testowy i usunął przed ceremonią. Przy ROTACJI będzie tam magazyn produkcyjny — ⛔ wtedy go nie
usuwasz: rotacja dopisuje klucz, nie zastępuje go (§15.3).*

**Wybierz jedną z dwóch dróg** (obie realizują A3; pierwsza jest mocniejsza):

- ⭐ **Droga A — dedykowana maszyna ceremonii.** Komputer **odłączony od sieci** na czas ceremonii (§24.1
  krok 1: *„offline"*), bez repozytorium, bez folderów synchronizowanych z chmurą, bez agenta backupu do
  chmury. Na niego kopiujesz wyłącznie zbudowany katalog `bin\Release\net9.0\` License Managera.
- **Droga B — dedykowane konto Windows** na obecnej maszynie. `%APPDATA%` jest per-użytkownik, więc nowe
  konto dostaje **pusty** folder, a devowy magazyn jest w innym profilu i jest nieosiągalny. ⚠ Słabsza:
  ta maszyna jest online i ma repozytorium. ⛔ Nie używaj konta z uprawnieniami administratora „na skróty".

**Gdzie powstają dane.** Nic jeszcze nie powstaje. Ustala się tylko, że
`%APPDATA%\EmberTern License Manager\` w środowisku ceremonii **nie istnieje albo jest pusty**.

**Co jest sekretem.** Nic.

**Czego nie wolno.** ⛔⛔ Nie usuwaj żadnego magazynu, którego nie potrafisz nazwać — usunięcie
produkcyjnego `keystore.etkeys` jest nieodwracalne i kończy możliwość odnawiania licencji. Magazyn
deweloperski wolno odstawić **dopiero** po ustaleniu, że nim jest, i to jest decyzja właściciela klucza, nie
wykonawcy kroku. ⛔ Nie kopiuj żadnego istniejącego magazynu do środowiska ceremonii „żeby mieć rejestr".

**Jak weryfikujemy.** W środowisku ceremonii, **przed** uruchomieniem aplikacji:

```bash
dir "%APPDATA%\EmberTern License Manager"
```

Musi zgłosić brak folderu albo pustą listę. ⭐ Po uruchomieniu License Managera pierwszy ekran musi mieć
nagłówek **„Utwórz klucz podpisujący"** (`UnlockCatalog.HeadlineCreate`), nie „Odblokuj". Nagłówek
„Odblokuj" oznacza, że w tej lokalizacji już jest magazyn — ⛔ **przerwij ceremonię** i wróć do kroku 1.

Binaria: zbuduj na maszynie deweloperskiej i skopiuj katalog wyjściowy.

```bash
dotnet build EmberTern.LicenseManager.slnx -c Release
```

Plik do uruchomienia: `src\EmberTern.LicenseManager\bin\Release\net9.0\EmberTern.LicenseManager.exe`.

---

## Krok 2 — Wygenerowanie pary P-256 i `kid` `R1`

**Co wykonujemy.** Na pierwszym ekranie License Managera: `kid` = **`R1`** (wartość domyślna; „root,
first", zgodna z rejestrem §35.4), hasło dostępu z kroku 3, powtórzone, i **Utwórz klucz podpisujący**.
Pod spodem `KeyCeremony.Perform` generuje ECDSA P-256 przez `ECDsa.Create()` (CSPRNG platformy), zapina
klucz prywatny w `keystore.etkeys` i od razu weryfikuje własne wyjście.

⛔ **Nie generuj klucza żadnym innym narzędziem** — nie `openssl`, nie skryptem, nie ręcznie. Ta ścieżka
jest jedyną, która natychmiast sprawdza, że wygenerowany klucz podpisuje licencję przechodzącą weryfikację
klientem, i jedyną, która produkuje wpis `TrustedKey` bez przepisywania (§15.1 punkt 2: ⛔ nie
dłubiemy w podpisywaniu).

**Gdzie powstają dane.** `%APPDATA%\EmberTern License Manager\keystore.etkeys` w środowisku ceremonii —
zapis atomowy przez plik `.tmp` i `File.Move`/`File.Replace`, żeby przerwany zapis nie zostawił połowy
magazynu tam, gdzie była jedyna kopia klucza. Powstaje też pusty `licenses.db`.

**Co jest sekretem.** ⛔ Cały `keystore.etkeys`. To **jedyna kopia** klucza prywatnego, dopóki nie wykonasz
kroku 5.

**Czego nie wolno.** ⛔ Nie wklejaj zawartości pliku nigdzie. ⛔ Nie rób zrzutu ekranu z wpisanym hasłem.
⛔ Nie zmieniaj `kid` na coś innego niż `R1` bez wpisania tej decyzji do §35.4 — `kid` jedzie w **każdej**
licencji i jest kluczem wyszukiwania w tabeli zaufanych kluczy (§15.2).

**Jak weryfikujemy.** Aplikacja wchodzi do głównego okna, a pasek komunikatu mówi **„Signing with key
R1"** / **„Podpisywanie kluczem R1"** (`StatusCatalog.SigningWithKey`). Plik istnieje i ma ~700–800 B:

```bash
dir "%APPDATA%\EmberTern License Manager\keystore.etkeys"
```

⭐ Fakt, że aplikacja weszła dalej, **sam jest weryfikacją**: `SigningSession.Create` kończy się przez
`Unlock`, czyli magazyn został odszyfrowany tym hasłem, które właśnie wpisałeś.

---

## Krok 3 — Ustanowienie hasła dostępu

**Co wykonujemy.** Hasło **wygenerowane**, nie wymyślone: **co najmniej sześć słów diceware**
(§24.1 krok 3). Generuje je menedżer haseł albo fizyczne kostki — ⛔ nigdy głowa.

⚠ Aplikacja wymusza jedynie **12 znaków** (`UnlockViewModel`). To podłoga techniczna, nie polityka:
prawdziwym wymaganiem jest sześć losowych słów, a komunikat aplikacji mówi to wprost
(`StatusCatalog.NewKeyPassphraseHint`).

**Gdzie powstają dane.** W generatorze. Wpisywane w dwa pola pierwszego ekranu (drugie to potwierdzenie —
istnieje, bo źle wpisane hasło do magazynu odkrywa się dopiero za rok).

**Co jest sekretem.** ⛔⛔ Samo hasło. **Nie ma resetu i nie ma tylnego wejścia.**

**Czego nie wolno.** ⛔ Nie w repo, nie w czacie, nie w mailu, nie w notatniku na pulpicie, nie w
komentarzu w kodzie, nie w zmiennej środowiskowej, nie w skrypcie. ⛔ Nie używaj tego samego sekretu co
hasło SMTP ani co hasło kopii zapasowej rejestru — §24.2 rozdziela je celowo: jeden ma być przenośny do
backupu, drugi ma nie być przenośny w ogóle (DPAPI). ⛔ Nie „uprość" go do czegoś, co da się wpisać z
pamięci.

**Jak weryfikujemy.** Krok 2 się udał, czyli hasło otwiera magazyn. Prawdziwym testem jest krok 6:
odtworzenie z kopii na drugim komputerze, **wpisując hasło z zapisu, nie z pamięci**.

---

## Krok 4 — Zapis i zabezpieczenie hasła dostępu

**Co wykonujemy.** Dwa niezależne nośniki (§24.1 krok 3):

1. **menedżer haseł** — wpis nazwany jednoznacznie (np. *„EmberTern — hasło dostępu magazynu kluczy
   podpisujących, kid R1, ceremonia RRRR-MM-DD"*);
2. **papier**, w **zaklejonej kopercie**, przechowywany **poza** lokalizacją, w której leżą kopie z kroku 5.

⭐ Na papierze zapisz też **odcisk klucza publicznego** i datę ceremonii. Odcisk nie jest sekretem, a
koperta jest jedynym miejscem, gdzie hasło i odcisk są razem — to pozwala potwierdzić po latach, że ta
koperta należy do tego klucza.

**Gdzie powstają dane.** Menedżer haseł + papier. ⛔ Nigdzie indziej.

**Co jest sekretem.** Oba nośniki.

**Czego nie wolno.** ⛔ Nie fotografuj kartki. ⛔ Nie wpisuj hasła do wspólnego/zespołowego sejfu haseł bez
odrębnej decyzji — §25.2 stawia maszynę administratora w strefie zaufanej, a nie „zespół".
⛔ Nie trzymaj koperty w tej samej szafie co nośniki z kroku 5: to jedno zdarzenie niszczące dwa razy.

**Jak weryfikujemy.** ⭐ **Odczytaj hasło z menedżera haseł i z papieru i porównaj znak po znaku** —
zanim zamkniesz kopertę. Papierowa kopia z jedną literówką jest gorsza niż jej brak, bo tworzy fałszywe
poczucie zabezpieczenia. Kryterium ostateczne: krok 6 przechodzi, wpisując hasło **z zapisu**.

---

## Krok 5 — Dwie kopie zapasowe magazynu kluczy

**Co wykonujemy.** Kopiujesz `keystore.etkeys` na **dwa nośniki offline w dwóch fizycznych lokalizacjach**
(§24.1 krok 4). Zwykłe kopiowanie pliku — plik jest niezależny od lokalizacji i już zaszyfrowany.

**Gdzie powstają dane.** Nośnik #1 i nośnik #2. Nazwij pliki tak, żeby po latach było wiadomo, co to jest,
np. `EmberTern-keystore-R1-RRRR-MM-DD.etkeys`. ⭐ Dołóż na każdy nośnik **plik tekstowy** z `kid`, odciskiem
i datą ceremonii — same wartości publiczne, a bez nich nie da się później stwierdzić, którego klucza jest
ta kopia, nie znając hasła.

**Co jest sekretem.** ⛔ Oba nośniki. Traktuj je jak klucz prywatny, którym są.

**Czego nie wolno.** ⛔ Nie na dysk sieciowy, nie na OneDrive/Dropbox/Google Drive, nie do kopii chmurowej,
nie do CI, nie jako załącznik mailowy — nawet zaszyfrowany (§24.2). ⛔ Nie „na razie na pulpit, przełożę
później". ⛔ Nie zostawiaj czwartej kopii w folderze `Pobrane` maszyny ceremonii.

**Jak weryfikujemy.** Rozmiar i skrót plików zgodne z oryginałem:

```bash
certutil -hashfile "%APPDATA%\EmberTern License Manager\keystore.etkeys" SHA256
```

Ten sam skrót na obu nośnikach. ⚠⚠ **Zgodność skrótu dowodzi tylko, że plik się skopiował — nie że jest
używalny.** To dowodzi krok 6, i dopóki go nie zrobisz, kopia jest **hipotezą** (§24.1 krok 5).

---

## Krok 6 — Odtworzenie każdej kopii na drugim komputerze

**Co wykonujemy.** ⭐⭐ **Ten krok jest sensem całej ceremonii i to jego się pomija.** Dla **każdej** z dwóch
kopii, na **drugim komputerze**:

1. skopiuj kopię do `%APPDATA%\EmberTern License Manager\keystore.etkeys` na tym komputerze (folder musi
   być pusty — jeśli nie jest, wróć do kroku 1);
2. uruchom License Managera i **odblokuj** magazyn, wpisując hasło **z zapisu z kroku 4**, nie z pamięci;
3. otwórz **Storage** (przycisk na pasku tytułu) → zakładka **Klucz podpisujący**;
4. przeczytaj `kid` i **odcisk** i porównaj z zapisem;
5. ⭐ w tej samej zakładce wpisz hasło dostępu i uruchom **Sprawdź kopię zapasową…**, wskazując **drugą**
   kopię.

⭐⭐ Punkt 5 jest tym, co czyni ten krok dowodem, a nie oględzinami. `SigningKeyFacts.VerifyBackup` otwiera
wskazany plik, sprawdza, że zawiera **ten sam** klucz co odblokowany właśnie magazyn, **podpisuje nim
licencję próbną i tę licencję weryfikuje** prawdziwym `LicenseVerifier`. ⚠ Klucz oczekiwany **nie jest
parametrem** tej operacji — nie ma wywołania, które mogłoby podać zły (dowodzi tego
`VerifyBackup_BindsTheExpectedKeyAndCannotBeToldOtherwise`). Kopia **innego** klucza otwiera się i ma
właściwy `kid`, a jest dokładnie tak samo bezużyteczna jak brak kopii — i to jedyny sposób, żeby to wyszło
teraz, a nie za rok.

Potem **odwrotnie**: magazynem staje się kopia #2, a sprawdzana jest kopia #1. Po obu przebiegach
⛔ **usuń `keystore.etkeys` z drugiego komputera** — nie jest to trwałe stanowisko wystawiania.

**Gdzie powstają dane.** Tymczasowo `%APPDATA%\EmberTern License Manager\` drugiego komputera. ⚠ Powstaje
tam też pusty `licenses.db`; usuń cały folder po zakończeniu.

**Co jest sekretem.** Odtworzony magazyn na drugim komputerze **przez cały czas trwania kroku**, oraz
wpisywane hasło.

**Czego nie wolno.** ⛔ Nie zostawiaj magazynu na drugim komputerze. ⛔ Nie rób „sprawdzenia kopii" na
maszynie ceremonii jako zamiennika — §24.1 mówi *„na innym komputerze"*, bo to jest test przenośności, a
nie test pliku. ⛔ Nie wklejaj żadnego komunikatu z tego kroku do czatu, jeśli zawiera ścieżkę do sekretu.

**Jak weryfikujemy.** Pasek komunikatu musi powiedzieć, że kopia jest **sprawna**, i **podać odcisk**:

> *Kopia zapasowa …\EmberTern-keystore-R1-…etkeys jest sprawna: otworzyła się, zawiera oczekiwany klucz i
> podpisała licencję, która przechodzi weryfikację. Odcisk `<64 znaki HEX>`.*

⛔ Każdy inny komunikat = ceremonia **nie jest zamknięta**. W szczególności *„zawiera INNY klucz"* oznacza,
że jedna z kopii nie jest kopią tego klucza. Cztery komunikaty tej operacji są rozłączne i pinuje to
`EachVerificationOutcome_SaysSomethingDifferent`.

⚠ **Znane ograniczenie, świadomie zaakceptowane w L7.1:** *„nie otworzyła się"* nie rozdziela złego hasła
od uszkodzonego pliku — `KeyCeremony.VerifyRestore` zwija tę klasyfikację do angielskiego pola
diagnostycznego, którego nie pokazujemy. Następny ruch jest w obu przypadkach ten sam (wpisz hasło
ponownie, potem sprawdź drugą kopię) i komunikat to mówi.

---

## Krok 7 — Porównanie odcisków

**Co wykonujemy.** Sprowadzamy do jednej wartości cztery niezależne odczyty:

| Źródło odcisku | Skąd |
|---|---|
| ceremonia | zakładka **Klucz podpisujący** w środowisku ceremonii (krok 2) |
| kopia #1 | odblokowana na drugim komputerze (krok 6) |
| kopia #2 | odblokowana na drugim komputerze (krok 6) |
| papier / menedżer haseł | zapis z kroku 4 |

⭐ Wszystkie cztery muszą być **identyczne**, 64 znaki HEX. Odcisk to SHA-256 nad DER SubjectPublicKeyInfo,
wielkimi literami.

**Gdzie powstają dane.** Nic nie powstaje — to porównanie.

**Co jest sekretem.** Nic. **Odcisk jest wartością publiczną** i wolno go skopiować, wkleić tutaj,
wydrukować i wpisać do repozytorium.

**Czego nie wolno.** ⛔ Nie porównuj „na oko" pierwszych i ostatnich sześciu znaków. Użyj **Kopiuj** przy
odcisku i porównaj mechanicznie (`fc`, wklejenie do jednego pliku, cokolwiek co porównuje pełny ciąg).
Ryzyko #2 z §30 to właśnie ta pomyłka.

**Jak weryfikujemy.** Cztery odczyty, jedna wartość. ⛔ Jakakolwiek różnica = **przerwij**; nie idź do
kroku 8. Rozbieżność między ceremonią a kopią oznacza, że kopia jest innego klucza (krok 5 od nowa);
rozbieżność między zapisem a resztą oznacza literówkę w zapisie (krok 4 od nowa).

---

## Krok 8 — Przygotowanie wpisu `TrustedKey`

**Co wykonujemy.** W zakładce **Klucz podpisujący**: **Kopiuj** przy *„Wpis do TrustedKeys.Production"*.
Dostajesz gotowy C#, np.:

```csharp
new TrustedKey("R1", SignatureAlgorithm.EcdsaP256Sha256, Convert.FromBase64String(
    "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE………" +
    "………")),
```

⭐ **Generowany, nie przepisywany** — to cały powód istnienia `KeyCeremony.FormatTrustedKeyEntry`. Skopiuj
też **klucz publiczny (SPKI, base64)** i **odcisk**: te dwie wartości idą do rejestru §35.4.

**Gdzie powstają dane.** Schowek maszyny ceremonii → plik tekstowy → nośnik przenoszący do maszyny
deweloperskiej. ⚠ Jeśli szłaś drogą A (maszyna offline), nośnikiem jest pendrive; jeśli drogą B, wystarczy
schowek między kontami.

**Co jest sekretem.** ⛔ **Nic z tego.** To jest połowa, która ma trafić do każdego klienta. ⚠ Ale nośnik
przenoszący **nie może** być tym samym pendrive'em, na którym leży kopia magazynu.

**Czego nie wolno.** ⛔ Nie przepisuj base64 ręcznie. ⛔ Nie „popraw" formatowania wpisu, nie skracaj, nie
łam linii inaczej. ⛔ Nie wynoś przy tej okazji `keystore.etkeys`.

**Jak weryfikujemy.** Wpis zawiera `"R1"`, `SignatureAlgorithm.EcdsaP256Sha256` i base64 kończący się
`)),`. ⭐ Prawdziwa weryfikacja jest w kroku 10, mechaniczna: test przelicza odcisk **z wklejonego klucza**
i porównuje z odciskiem z kroku 7, więc pomyłka w transkrypcji **wywala build**, a nie klienta.

---

## Krok 9 — Wpisanie klucza publicznego do `TrustedKeys.Production`

**Co wykonujemy.** Na maszynie deweloperskiej, w
[`src/EmberTern.Licensing/TrustedKeys.cs`](../../src/EmberTern.Licensing/TrustedKeys.cs), wklejasz wpis w
miejsce dwóch komentarzy w inicjalizatorze `Production`.

⛔ **Tabela jest APPEND-ONLY.** Wpis nigdy nie jest usuwany ani edytowany — wyłącznie dopisywany albo
oznaczany `Revoked` (§15.3). Klucz usunięty z tej tabeli to populacja licencji, które przestały działać na
maszynach, do których nie mamy dostępu.

Zaktualizuj też komentarz klasy: dziś mówi *„It is EMPTY today, and that is correct for stage L1"* — po
ceremonii to zdanie jest nieprawdą, a nieprawdziwy komentarz o stanie tymczasowym jest dokumentacyjną
wersją pułapki z gotchy #407. To samo dotyczy czterech komentarzy mówiących *„empty until L7"*:
[`LicenseService.cs:48-50`](../../src/EmberTern.App/Licensing/LicenseService.cs),
`LicenseFixtures.cs:17`, `LicenseGateTests.cs:24`, `LicenseServiceTests.cs:17`/`:147`,
`LicenseTestFactory.cs:16`.

**Gdzie powstają dane.** Jeden plik źródłowy w repozytorium.

**Co jest sekretem.** ⛔ Nic — i to jest właśnie granica, której pilnuje `PrivateKeyNeverShipsTests`. To
jedyna wartość z całej ceremonii, która **ma** wejść do repo.

**Czego nie wolno.** ⛔⛔ Nigdy nie wklej tu klucza **prywatnego** — sześć strażników `PrivateKeyNeverShips`
wywali build, ale to nie jest powód, żeby na nich polegać. ⛔ Nie dodawaj drugiego wpisu „testowego"
(decyzja Option A z §37.1: jedna tabela, produkcyjna; testy używają własnych). ⛔ Nie dotykaj istniejących
wpisów — dziś nie ma żadnego, więc `R1` jest pierwszy.

**Jak weryfikujemy.** Build obu rozwiązań, obie konfiguracje. ⭐ `TrustedKeyTable` waliduje każdy wpis **w
konstruktorze** (czytelny SPKI, dokładnie 256 bitów, unikalny `kid`), więc uszkodzony klucz jest wyjątkiem
przy pierwszym użyciu tabeli, a nie „nieprawidłową licencją" pokazaną użytkownikowi.

---

## Krok 10 — Wymiana testu `TheShippedTrustedKeyTableIsStillEmptyAtThisStage`

**Co wykonujemy.** [`LicenseVerifierTests.cs:146`](../../tests/EmberTern.Tests/LicenseVerifierTests.cs)
zawiera `Assert.Empty(TrustedKeys.Production.Keys)`. ⭐ **To jedyny test w obu pakietach, który zależy od
pustej tabeli** (zmierzone, nie założone) i jego własny komentarz mówi, co z nim zrobić: *„when L2 adds the
first key, update it to assert the key is present, non-revoked and P-256. ⛔ Do not delete it."*

⚠⚠ **Wymiana należy do TEJ SAMEJ zmiany co krok 9**, nie do następnej sesji. Gotcha #407: strażnik pinujący
stan świadomie tymczasowy w dniu wygaśnięcia tego stanu wygląda **dokładnie jak regresja**, a test o nazwie
„…IsStillEmptyAtThisStage" na czerwono czyta się jako *„zepsułeś tabelę kluczy"*, a nie *„ta obietnica
wygasła".*

Nowy test twierdzi **oba half-y**, bo każdy z osobna przechodzi na zepsutym kluczu:

1. tabela ma dokładnie jeden wpis, `kid` `R1`, nie `Revoked`, algorytm `EcdsaP256Sha256`, klucz
   importowalny jako 256-bitowy SPKI;
2. ⭐ **odcisk wysłanego klucza równa się odciskowi zapisanemu w §35.4** — literał 64 znaków HEX w teście.
   To jest mechaniczne domknięcie ryzyka #2: rozjazd między wklejonym kluczem a zapisem ceremonii
   **wywala build**.

⚠ `EmberTern.Tests` nie może odwołać się do `KeyCeremony` (jest w rozwiązaniu wystawcy, i to jest cała
istota `PrivateKeyNeverShipsTests`), więc odcisk liczy się w teście przez `SHA256.HashData` nad
`SubjectPublicKeyInfo` — ta sama definicja, druga implementacja, i to jest zaleta, nie duplikat.

**Gdzie powstają dane.** Jeden plik testowy.

**Co jest sekretem.** Nic. Odcisk jest publiczny.

**Czego nie wolno.** ⛔ Nie usuwaj testu. ⛔ Nie osłabiaj do `Assert.NotEmpty` — to przechodzi dla
**dowolnego** klucza, w tym dla przypadkowo wklejonego devowego.

**Jak weryfikujemy.** Test zielony po kroku 9, a **czerwony** przy wstrzykniętej zmianie jednego znaku w
base64 albo w literale odcisku. ⭐ Tę czerwień trzeba zobaczyć — zielony strażnik, którego nikt nie widział
na czerwono, nie jest dowodem (§35.1).

---

## Krok 11 — Wystawienie prawdziwej licencji

**Co wykonujemy.** W środowisku ceremonii, w License Managerze: klient → licencja → warunki →
**Wystaw i zapisz…**. Artefakt trafia do rejestru **i** na dysk jako `EmberTern.etlic`.

⭐ To pierwsza licencja, która ma szansę zweryfikować się w shipowanym buildzie, bo dopiero teraz istnieje
klucz, który klient zna.

**Gdzie powstają dane.** `licenses.db` środowiska ceremonii (klient, licencja, artefakt, wskaźnik
`current`, wpis audytu) + plik `EmberTern.etlic` we wskazanym miejscu.

**Co jest sekretem.** ⚠ Licencja **nie jest** sekretem w sensie kryptograficznym — jest podpisanym
oświadczeniem i sama z siebie nie pozwala niczego podrobić. ⚠ Zawiera natomiast **dane klienta**
(nazwa licencjobiorcy), więc nie jest też materiałem do publikowania. ⛔ Do sprawdzenia w kroku 12 użyj
danych własnych/testowych, nie prawdziwego klienta.

**Czego nie wolno.** ⛔ Nie wysyłaj tej pierwszej licencji do klienta, dopóki krok 12 nie przejdzie:
dopóki nie widziałeś `Valid` w `Release`, nie wiesz, że łańcuch działa end-to-end.

**Jak weryfikujemy.** `IssuingWorkflow` weryfikuje własne wyjście przy wystawianiu (`LicenseIssuer.Issue`
sprawdza artefakt prawdziwym `LicenseVerifier` przeciw publicznej połowie klucza i rzuca, zamiast oddać
coś, czego nie potrafi udowodnić). Dodatkowo **Sprawdź najnowsze** w historii wystawień pokazuje werdykt.

---

## Krok 12 — Weryfikacja `Valid` w buildzie `Release`

**Co wykonujemy.** Na maszynie testowej (może być deweloperska): build **`Release`** EmberTerna
zawierający wpis z kroku 9, wgranie licencji przez okno aktywacji, i obejrzenie **`Valid`**.

```bash
dotnet build EmberTern.slnx -c Release
```

Aplikacja czyta licencję z `%APPDATA%\EmberTern\license.etlic` (per-użytkownik), z fallbackiem
`%PROGRAMDATA%\EmberTern\license.etlic` (per-maszyna, tylko do czytania). Okno aktywacji zapisuje
wyłącznie ścieżkę per-użytkownik.

⚠⚠ **Musi to być `Release`.** Bramka jest kompilowanym `const` (`LicensingPolicy.GateEnabled`), więc w
`Debug` blokada jest wyłączona i `Debug` **nie może** dowieść niczego o zachowaniu blokującym (§16.5).
⚠ Uruchamiaj build z `bin\Release\`, nie z `bin\Debug\` — pomyłka w tym miejscu kosztowała już w tym
projekcie jeden cykl przeglądu.

**Gdzie powstają dane.** `%APPDATA%\EmberTern\license.etlic` maszyny testowej + wpis
`LicenseClockHighWater` w `settings.dat`.

**Co jest sekretem.** Nic nowego.

**Czego nie wolno.** ⛔ Nie kopiuj `keystore.etkeys` na maszynę testową — do sprawdzenia licencji klucz
prywatny jest niepotrzebny i to jest właśnie pointa (§25.2: klient tylko weryfikuje i nie trzyma sekretu).

**Jak weryfikujemy.** ⭐⭐ **To jest warunek wyjścia całej ceremonii i cała odłożona linia §38.6.**
Ustawienia ▸ Licencja pokazują **Licencja aktywna** / **Valid**, licencjobiorcę i daty ważności; okno About
pokazuje linię licencjobiorcy; aplikacja łączy się z bazą bez blokady. ⛔ `Invalid / UnknownKey` = klucz z
kroku 9 nie jest tym, którym wystawiono licencję — wróć do kroku 7.

---

## Krok 13 — Końcowa weryfikacja buildów i testów

**Co wykonujemy.** Cztery buildy i dwa pakiety testów, **totale zmierzone, nie przepisane**.

```bash
dotnet build EmberTern.slnx -c Debug
```
```bash
dotnet build EmberTern.slnx -c Release
```
```bash
dotnet build EmberTern.LicenseManager.slnx -c Debug
```
```bash
dotnet build EmberTern.LicenseManager.slnx -c Release
```
```bash
dotnet test EmberTern.slnx -c Debug
```
```bash
dotnet test EmberTern.LicenseManager.slnx -c Debug
```

⛔ Nigdy nie łącz `build` i `test` w jedno polecenie — zakleszczają się.

⭐ **Uruchom pakiet produktu także w `Release`** — to jedyne, co dowodzi ramienia `Release` bramki
(`LicensingGateTests.TheGateFollowsTheBuildConfiguration`).

**Gdzie powstają dane.** Katalogi `bin`/`obj`. Nic trwałego.

**Co jest sekretem.** Nic.

**Czego nie wolno.** ⛔ Nie akceptuj wyniku po samym „0 niepowodzeń": kryterium jest **TOTAL**. Zmierzony
stan wejściowy L7.1 to **9 092** (produkt) i **777** (License Manager). ⛔ Nie napraw przy tej okazji dwóch
znanych czerwonych — `CharsetGuardSeamTests.TheExcludedProjectsGenuinelyCannotReachTheFirebirdDriver` i
`DatePresentationTests.NoUserFacingSurface_FormatsADateInvariantly` **są pre-existing** i nie są L7.

**Jak weryfikujemy.** Buildy 0/0. Pakiet License Managera w pełni zielony. Pakiet produktu: total zgodny,
dokładnie te dwie znane czerwone i ani jedna więcej. ⚠ Jeden przegrany test **ze stosem**
`AvaloniaHeadlessPlatform.Initialize` → `Dispatcher.VerifyAccess` to znana wyścigówka upstreamu — powtórz
przebieg raz; ⛔ czerwony **bez** tego stosu jest prawdziwy.

---

## Krok 14 — Wpis do §35.4

**Co wykonujemy.** Uzupełniasz tabelę rejestru ceremonii w
[`licensing-system.md`](licensing-system.md) §35.4 — decyzja **D1**: ⛔ **bez osobnego „Appendix A"**, mimo
że §24.1 krok 7 tak go nazywa (ten dokument koryguje to nazewnictwo, nie tworzy drugiego rejestru).

| kid | Algorithm | Public key (SPKI, base64) | Ceremony date | Revoked |
|---|---|---|---|---|
| `R1` | ECDSA-P256-SHA256 | *(base64 z kroku 8)* | *(RRRR-MM-DD)* | — |

Dopisz pod tabelą **odcisk** (64 znaki HEX) i zdanie, że kopie zapasowe są dwie i że **każda** została
odtworzona i sprawdzona na drugim komputerze, z datą.

⚠ **Nagłówek kolumny mówi dziś „base64url", a kod produkuje zwykły base64** (`Convert.ToBase64String` w
`FormatTrustedKeyEntry`, `Convert.FromBase64String` przy odczycie). Kod jest kontraktem — popraw nagłówek,
⛔ nie wartość.

**Gdzie powstają dane.** Dokumentacja w repozytorium.

**Co jest sekretem.** ⛔ Nic z tej tabeli. Wszystko w niej jest publiczne. ⛔⛔ **Nie wpisuj tu, gdzie leżą
kopie zapasowe ani jak nazywa się wpis w menedżerze haseł** — repozytorium jest zmirrorowane na dwa
zdalne serwery i mapa lokalizacji sekretu nie jest wartością publiczną.

**Czego nie wolno.** ⛔ Nie edytuj tego wiersza po latach przy rotacji — §15.3 mówi *dopisuj i flaguj*;
`R2` dostaje **własny** wiersz.

**Jak weryfikujemy.** Odcisk w §35.4 == literał w teście z kroku 10 == zapis papierowy z kroku 4. ⭐ Dwa
pierwsze pilnuje build; trzeci pilnujesz Ty.

---

## Domknięcie L7 po ceremonii

⚠ Poza samą ceremonią L7 wciąż jest winien (§32 + zaległości):

| Pozycja | Stan |
|---|---|
| clock high-water | ✅ zaimplementowane w L4a. ⏭ **Ostrzeżenie** o cofnięciu zegara — świadomy backlog (decyzja **C2**), `ClockLooksRolledBack` nie ma powierzchni |
| `%PROGRAMDATA%` fallback | ✅ zaimplementowane w L4a (`LicenseLocation`) |
| bramka `maint` | ✅ zaimplementowana w L4a (`LicenseVerifier`) |
| gotcha zaległa od L2 (§35.3) | 📋 do dopisania: `Utf8JsonWriter` escapuje `+` jako `+`, więc base64 nie występuje w pliku dosłownie → **edytuj JSON jako JSON (`JsonNode`), nigdy jako tekst**. Groźny kształt: poprawny produkt w czerwonym teście |
| gotcha z L7.1 | 📋 do rozważenia: *funkcja, której wynik nikt nie odbiera, jest funkcją nieistniejącą* — `KeyCeremony` liczył odcisk i wpis od L2, a `SigningSession.Create` je wyrzucał; kroki 5 i 7 §24.1 nie miały wykonawcy w aplikacji, a pełny zestaw testów świecił zielono |
| `docs/history/` | 📋 pierwszy plik narracji licencjonowania (dziś **nie ma żadnego** — cała narracja L1–L8 jest w design docu, 5 087 linii) |
| `docs/current-state.md` | 📋 jedna linia; 292/300 linii, więc coś musi wyjść do `history/` |
| `CLAUDE.md` | ⚠ **825 linii** przeciw progowi ~800 + zaległa jedna linia z §38.8 + wiersz mapy dokumentacji dla tego runbooka. ⛔ Sprzątanie `CLAUDE.md` to **osobne zadanie** i L7 go nie rozszerza |
| push | 📋 `origin`, potem `private` — **po Twojej akceptacji** |
