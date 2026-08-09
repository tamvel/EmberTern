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
