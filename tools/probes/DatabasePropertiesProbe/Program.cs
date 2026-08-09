// Pakiet UX po M5, punkt 6 (Database Properties) — KROK 0: sonda pomiarowa.
//
//   dotnet run --project tools/probes/DatabasePropertiesProbe -- <sysdba-password>
//
// Odpowiada na pięć pytań, których NIE WOLNO wydedukować (CLAUDE.md: „verify Firebird behaviour, never
// infer it"), bo każde z nich decyduje o tym, które pola dialogu mogą być edytowalne:
//
//   A. Jak NAPRAWDĘ nazywają się kolumny źródeł (`MON$DATABASE`, `RDB$DATABASE`) na żywym FB5.
//   B. Jakie są RZECZYWISTE sygnatury `FbConfiguration` — znalezienie nazwy metody w binarce dowodzi
//      istnienia SYMBOLU, nigdy działania (gotcha #321 w tym samym kształcie).
//   C. Czy zapis działa ONLINE (przy otwartym attachmencie) i KIEDY zaczyna obowiązywać.
//   D. Jak zachowuje się uwierzytelnianie Services API (poprawne / puste / błędne hasło).
//   E. Co widzi użytkownik bez uprawnień.
//
// ⚠ `Pooling = false` w KAŻDYM connection stringu jest warunkiem poprawności pomiaru, nie ostrożnością:
//   pytanie „czy zmiana obowiązuje dopiero dla kolejnego attachmentu" jest bez sensu, jeżeli pula oddaje
//   to samo połączenie. Bez tego sonda odpowiadałaby na inne pytanie, niż zadano.
//
// ⚠ Wszystko dzieje się na bazie SCRATCH pod ścieżką ASCII. Lab nie jest dotykany.

using System.Data;
using System.Reflection;
using System.Text;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;

// ⚠ ODTWORZONE PRZEZ PIERWSZY PRZEBIEG, nie z lektury: bez tego CREATE DATABASE z charsetem WIN1250
//   pada na "Invalid character set specified". W aplikacji rejestracja siedzi w statycznym konstruktorze
//   FirebirdConnectionService (i CharsetCatalog), więc każdy NOWY punkt wejścia, który świadomie omija
//   nasze opakowania, musi ją powtórzyć. To dowód, że rejestracja jest własnością PUNKTU WEJŚCIA, a nie
//   procesu — wart zapisania, bo dotyczy każdej przyszłej sondy i każdego narzędzia CLI.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var password = args.Length > 0 ? args[0] : "masterkey";
var scratchPath = @"C:\Temp\embertern_dbprops.fdb";
Directory.CreateDirectory(@"C:\Temp");

Console.WriteLine("Database Properties — KROK 0 (sonda pomiarowa)");
Console.WriteLine($"Baza scratch: {scratchPath}");
Console.WriteLine();

FbConnectionStringBuilder Db() => new()
{
    DataSource = "localhost",
    Port = 3050,
    Database = scratchPath,
    UserID = "SYSDBA",
    Password = password,
    Charset = "WIN1250",
    Dialect = 3,
    ServerType = FbServerType.Default,
    Pooling = false,
};

// Services connection string — kształt wzięty z `FirebirdTraceService.BuildServiceConnectionString`
// (host / port / user / password / ServerType.Default), plus `Database`, bo operacje `FbConfiguration`
// dotyczą KONKRETNEJ bazy, a nie serwera. Czy `Database` jest wymagane — mierzy sekcja D.
string Svc(string? pwd = null, bool withDatabase = true)
{
    var b = new FbConnectionStringBuilder
    {
        DataSource = "localhost",
        Port = 3050,
        UserID = "SYSDBA",
        Password = pwd ?? password,
        ServerType = FbServerType.Default,
    };
    if (withDatabase) b.Database = scratchPath;
    return b.ToString();
}

void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine(new string('=', 100));
    Console.WriteLine(title);
    Console.WriteLine(new string('=', 100));
}

static string Short(Exception ex)
{
    var m = ex.Message.Replace("\r", " ").Replace("\n", " ").Trim();
    if (ex is FbException fb)
    {
        var codes = string.Join("/", fb.Errors.Cast<FbError>().Select(e => e.Number.ToString()));
        m = $"[SQLSTATE {fb.SQLSTATE}] [GDS {codes}] {m}";
    }

    return m.Length > 220 ? m[..220] + " …" : m;
}

async Task<object?> ScalarAsync(FbConnection c, string sql)
{
    await using var cmd = new FbCommand(sql, c);
    return await cmd.ExecuteScalarAsync();
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────
FbConnection.CreateDatabase(Db().ToString(), overwrite: true);
Console.WriteLine("Baza scratch utworzona (WIN1250, dialect 3) — jak lab.");

// ═════ A. ŹRÓDŁA ODCZYTU — rzeczywiste nazwy kolumn ═══════════════════════════════════════════════════
Section("A. ŹRÓDŁA ODCZYTU — jakie kolumny FB5 NAPRAWDĘ udostępnia");

await using (var c = new FbConnection(Db().ToString()))
{
    await c.OpenAsync();

    foreach (var rel in new[] { "MON$DATABASE", "RDB$DATABASE" })
    {
        Console.WriteLine();
        Console.WriteLine($"--- {rel} — kolumny wg katalogu ---");
        await using var cmd = new FbCommand(
            "SELECT TRIM(rf.RDB$FIELD_NAME), f.RDB$FIELD_TYPE, f.RDB$FIELD_LENGTH " +
            "FROM RDB$RELATION_FIELDS rf JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE " +
            "WHERE rf.RDB$RELATION_NAME = @r ORDER BY rf.RDB$FIELD_POSITION", c);
        cmd.Parameters.AddWithValue("@r", rel);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            Console.WriteLine($"    {r.GetString(0),-32} typ={r.GetValue(1),-4} dl={r.GetValue(2)}");
        }
    }

    // Wartości — dokładnie te, które dialog miałby pokazać.
    Console.WriteLine();
    Console.WriteLine("--- Wartości odczytane z żywej bazy ---");
    await using (var cmd = new FbCommand("SELECT * FROM MON$DATABASE", c))
    await using (var r = await cmd.ExecuteReaderAsync())
    {
        if (await r.ReadAsync())
        {
            for (var i = 0; i < r.FieldCount; i++)
            {
                Console.WriteLine($"    MON$ {r.GetName(i),-30} = {(r.IsDBNull(i) ? "<null>" : r.GetValue(i))}");
            }
        }
    }

    foreach (var (label, sql) in new[]
             {
                 ("RDB$DATABASE.RDB$CHARACTER_SET_NAME",
                     "SELECT TRIM(RDB$CHARACTER_SET_NAME) FROM RDB$DATABASE"),
                 ("RDB$DATABASE.RDB$LINGER", "SELECT RDB$LINGER FROM RDB$DATABASE"),
                 ("RDB$DATABASE.RDB$DESCRIPTION", "SELECT RDB$DESCRIPTION FROM RDB$DATABASE"),
                 ("ENGINE_VERSION (context)",
                     "SELECT RDB$GET_CONTEXT('SYSTEM','ENGINE_VERSION') FROM RDB$DATABASE"),
                 ("DB_NAME (context)", "SELECT RDB$GET_CONTEXT('SYSTEM','DB_NAME') FROM RDB$DATABASE"),
             })
    {
        try { Console.WriteLine($"    {label,-40} = {await ScalarAsync(c, sql) ?? "<null>"}"); }
        catch (Exception ex) { Console.WriteLine($"    {label,-40} ⛔ {Short(ex)}"); }
    }

    Console.WriteLine($"    {"FbConnection.ServerVersion (sterownik)",-40} = {c.ServerVersion}");
}

// ═════ B. FbConfiguration — rzeczywiste sygnatury ═════════════════════════════════════════════════════
Section("B. FbConfiguration — RZECZYWISTE sygnatury (refleksja w działającym procesie)");

var cfgType = typeof(FbConfiguration);
Console.WriteLine($"Typ:      {cfgType.FullName}");
Console.WriteLine($"Assembly: {cfgType.Assembly.GetName().Name} {cfgType.Assembly.GetName().Version}");
Console.WriteLine($"Bazowy:   {cfgType.BaseType?.FullName}");
Console.WriteLine();
Console.WriteLine("Konstruktory:");
foreach (var ctor in cfgType.GetConstructors())
{
    Console.WriteLine("    ctor(" + string.Join(", ",
        ctor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")) + ")");
}

Console.WriteLine();
Console.WriteLine("Metody Set* (wraz z odziedziczonymi):");
foreach (var m in cfgType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
             .Where(m => m.Name.StartsWith("Set", StringComparison.Ordinal))
             .OrderBy(m => m.Name))
{
    Console.WriteLine($"    {m.ReturnType.Name} {m.Name}(" + string.Join(", ",
        m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")) + ")");
}

Console.WriteLine();
Console.WriteLine("Właściwości (skąd bierze połączenie):");
foreach (var p in cfgType.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name))
{
    Console.WriteLine($"    {p.PropertyType.Name,-24} {p.Name,-24} zapisywalna={p.CanWrite} (deklaruje {p.DeclaringType?.Name})");
}

// ═════ C. ZAPIS — online, moment zadziałania, przywrócenie ════════════════════════════════════════════
Section("C. ZAPIS — czy działa ONLINE i KIEDY zaczyna obowiązywać");
Console.WriteLine("Każda próba: odczyt → zapis (przy OTWARTYM attachmencie) → odczyt na TYM SAMYM połączeniu");
Console.WriteLine("→ odczyt na NOWYM połączeniu → przywrócenie wartości pierwotnej.");
Console.WriteLine("⚠ Pooling wyłączony, więc „nowe połączenie\" naprawdę oznacza nowy attachment.");

// Trzyma attachment otwarty przez cały czas trwania sekcji — to jest cała treść pytania „czy online".
await using var held = new FbConnection(Db().ToString());
await held.OpenAsync();
Console.WriteLine($"\nAttachment trzymany otwarty: MON$ATTACHMENT_ID = " +
                  $"{await ScalarAsync(held, "SELECT CURRENT_CONNECTION FROM RDB$DATABASE")}");

async Task<object?> ReadOnFresh(string sql)
{
    await using var c = new FbConnection(Db().ToString());
    await c.OpenAsync();
    return await ScalarAsync(c, sql);
}

async Task Measure(string what, string readSql, Func<FbConfiguration, Task> write, Func<FbConfiguration, Task> restore)
{
    Console.WriteLine();
    Console.WriteLine($"--- {what} ---");
    object? before = null;
    try
    {
        before = await ScalarAsync(held, readSql);
        Console.WriteLine($"    przed                        : {before}");
    }
    catch (Exception ex) { Console.WriteLine($"    odczyt ⛔ {Short(ex)}"); return; }

    try
    {
        var svc = new FbConfiguration(Svc());
        await write(svc);
        Console.WriteLine("    zapis przy otwartym attach.  : OK (bez wyjątku)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"    zapis przy otwartym attach.  : ⛔ ODRZUCONY — {Short(ex)}");
        return;
    }

    try { Console.WriteLine($"    po zapisie, TO SAMO połącz.  : {await ScalarAsync(held, readSql)}"); }
    catch (Exception ex) { Console.WriteLine($"    po zapisie, to samo   ⛔ {Short(ex)}"); }

    try { Console.WriteLine($"    po zapisie, NOWY attachment  : {await ReadOnFresh(readSql)}"); }
    catch (Exception ex) { Console.WriteLine($"    po zapisie, nowy      ⛔ {Short(ex)}"); }

    try
    {
        var svc = new FbConfiguration(Svc());
        await restore(svc);
        Console.WriteLine($"    przywrócono do              : {await ReadOnFresh(readSql)}");
    }
    catch (Exception ex) { Console.WriteLine($"    ⛔⛔ PRZYWRÓCENIE NIEUDANE — {Short(ex)}"); }
}

await Measure("Sweep interval  (SetSweepIntervalAsync)",
    "SELECT MON$SWEEP_INTERVAL FROM MON$DATABASE",
    s => s.SetSweepIntervalAsync(12345),
    s => s.SetSweepIntervalAsync(20000));

await Measure("Forced writes   (SetForcedWritesAsync)",
    "SELECT MON$FORCED_WRITES FROM MON$DATABASE",
    s => s.SetForcedWritesAsync(false),
    s => s.SetForcedWritesAsync(true));

await Measure("Page buffers    (SetPageBuffersAsync)",
    "SELECT MON$PAGE_BUFFERS FROM MON$DATABASE",
    s => s.SetPageBuffersAsync(512),
    s => s.SetPageBuffersAsync(2048));

await Measure("Reserve space   (SetReserveSpaceAsync)",
    "SELECT MON$RESERVE_SPACE FROM MON$DATABASE",
    s => s.SetReserveSpaceAsync(false),
    s => s.SetReserveSpaceAsync(true));

// ⚠ Dialect i Read Only mierzone tak samo, ale z JAWNYM przywróceniem — użytkownik wprost zażądał,
//   żeby baza nie została w innym dialekcie.
await Measure("SQL dialect     (SetSqlDialectAsync)",
    "SELECT MON$SQL_DIALECT FROM MON$DATABASE",
    s => s.SetSqlDialectAsync(1),
    s => s.SetSqlDialectAsync(3));

await Measure("Read only       (SetAccessModeAsync)",
    "SELECT MON$READ_ONLY FROM MON$DATABASE",
    s => s.SetAccessModeAsync(true),
    s => s.SetAccessModeAsync(false));

// To samo, ale BEZ trzymanego attachmentu — rozstrzyga, czy odmowa (jeśli była) dotyczy wyłączności.
Console.WriteLine();
Console.WriteLine("--- Read only PONOWNIE, po zamknięciu wszystkich naszych attachmentów ---");
await held.CloseAsync();
try
{
    var svc = new FbConfiguration(Svc());
    await svc.SetAccessModeAsync(true);
    Console.WriteLine("    zapis bez attachmentów       : OK — czyli wymagana była WYŁĄCZNOŚĆ");
    Console.WriteLine($"    odczyt                       : {await ReadOnFresh("SELECT MON$READ_ONLY FROM MON$DATABASE")}");
    await new FbConfiguration(Svc()).SetAccessModeAsync(false);
    Console.WriteLine($"    przywrócono do               : {await ReadOnFresh("SELECT MON$READ_ONLY FROM MON$DATABASE")}");
}
catch (Exception ex)
{
    Console.WriteLine($"    zapis bez attachmentów       : ⛔ nadal odrzucony — {Short(ex)}");
}

// ═════ D. UWIERZYTELNIANIE SERVICES API ══════════════════════════════════════════════════════════════
Section("D. UWIERZYTELNIANIE Services API");

async Task Auth(string what, string connectionString)
{
    try
    {
        var svc = new FbConfiguration(connectionString);
        await svc.SetSweepIntervalAsync(20000);
        Console.WriteLine($"    {what,-46} : OK");
    }
    catch (Exception ex) { Console.WriteLine($"    {what,-46} : ⛔ {Short(ex)}"); }
}

await Auth("poprawne hasło", Svc());
await Auth("PUSTE hasło (profil bez zapisanego hasła)", Svc(pwd: ""));
await Auth("błędne hasło", Svc(pwd: "definitely-not-the-password"));
await Auth("bez Database w connection stringu", Svc(withDatabase: false));

// ═════ E. UPRAWNIENIA ════════════════════════════════════════════════════════════════════════════════
Section("E. UPRAWNIENIA — użytkownik bez praw administracyjnych");

const string testUser = "ET_PROBE_USR";
var created = false;
try
{
    await using (var c = new FbConnection(Db().ToString()))
    {
        await c.OpenAsync();
        await using var cmd = new FbCommand(
            $"CREATE USER {testUser} PASSWORD 'probe-pwd-1' USING PLUGIN Srp", c);
        await cmd.ExecuteNonQueryAsync();
        created = true;
    }

    Console.WriteLine($"    utworzono użytkownika {testUser} (bez uprawnień administracyjnych)");

    var b = new FbConnectionStringBuilder
    {
        DataSource = "localhost",
        Port = 3050,
        Database = scratchPath,
        UserID = testUser,
        Password = "probe-pwd-1",
        ServerType = FbServerType.Default,
    };

    try
    {
        await new FbConfiguration(b.ToString()).SetSweepIntervalAsync(15000);
        Console.WriteLine("    zapis jako zwykły użytkownik : OK (!) — brak bramki uprawnień");
    }
    catch (Exception ex) { Console.WriteLine($"    zapis jako zwykły użytkownik : ⛔ {Short(ex)}"); }

    try
    {
        var dbb = Db();
        dbb.UserID = testUser;
        dbb.Password = "probe-pwd-1";
        await using var uc = new FbConnection(dbb.ToString());
        await uc.OpenAsync();
        Console.WriteLine($"    odczyt MON$DATABASE          : " +
                          $"{await ScalarAsync(uc, "SELECT MON$PAGE_SIZE FROM MON$DATABASE")}");
    }
    catch (Exception ex) { Console.WriteLine($"    odczyt MON$DATABASE          : ⛔ {Short(ex)}"); }
}
catch (Exception ex)
{
    Console.WriteLine($"    ⛔ nie udało się przygotować przypadku — {Short(ex)}");
    Console.WriteLine("    (pomiar NIEWYKONANY — zgłoszone jako niezmierzone, nie jako wynik)");
}
finally
{
    if (created)
    {
        try
        {
            await using var c = new FbConnection(Db().ToString());
            await c.OpenAsync();
            await using var cmd = new FbCommand($"DROP USER {testUser} USING PLUGIN Srp", c);
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"    użytkownik {testUser} usunięty");
        }
        catch (Exception ex) { Console.WriteLine($"    ⚠ NIE UDAŁO SIĘ USUNĄĆ {testUser}: {Short(ex)}"); }
    }
}

// ═════ F. PAGE BUFFERS — izolowany pomiar momentu zadziałania ════════════════════════════════════════
Section("F. PAGE BUFFERS — kiedy DOKŁADNIE zmiana zaczyna obowiązywać");
Console.WriteLine("⚠ Ta sekcja istnieje, bo w sekcji C page buffers ZACHOWAŁ SIĘ INACZEJ NIŻ WSZYSTKIE POZOSTAŁE:");
Console.WriteLine("  zapis nie rzucił wyjątku, ale ANI to samo połączenie, ANI nowy attachment nie zobaczyły nowej");
Console.WriteLine("  wartości — a stan końcowy pokazał ją już zmienioną. Sekwencja z C nie rozstrzyga, CO było");
Console.WriteLine("  zdarzeniem wyzwalającym; poniżej rozdzielone: „nowy attachment\" vs „pełne zwolnienie bazy\".");

// Świeża baza — żeby wartość wyjściowa nie była skutkiem wcześniejszych zapisów tej sondy.
FbConnection.CreateDatabase(Db().ToString(), overwrite: true);

const string pbSql = "SELECT MON$PAGE_BUFFERS FROM MON$DATABASE";
Console.WriteLine();
Console.WriteLine($"    1. świeża baza, pierwszy odczyt                    : {await ReadOnFresh(pbSql)}");

await using (var keeper = new FbConnection(Db().ToString()))
{
    await keeper.OpenAsync();
    Console.WriteLine("    2. attachment „keeper\" otwarty — baza pozostaje w użyciu");

    await new FbConfiguration(Svc()).SetPageBuffersAsync(1024);
    Console.WriteLine("    3. zapis SetPageBuffersAsync(1024)                 : OK");

    Console.WriteLine($"    4. odczyt na NOWYM attachmencie (baza wciąż w użyciu): {await ReadOnFresh(pbSql)}");
}

Console.WriteLine("    5. keeper zamknięty — baza w pełni zwolniona");
Console.WriteLine($"    6. odczyt na NOWYM attachmencie po zwolnieniu       : {await ReadOnFresh(pbSql)}");

// ═════ G. PAGE BUFFERS — czy „użyj domyślnej serwera" da się w ogóle przywrócić ══════════════════════
Section("G. PAGE BUFFERS — czy wartość 0 („dziedzicz domyślną serwera\") jest zapisywalna");
Console.WriteLine("⚠ Pytanie wynika wprost z F: skoro MON$ raportuje CACHE DZIAŁAJĄCY, a nie zapisany nagłówek,");
Console.WriteLine("  to pole edycji zasiane z MON$ pokazałoby 51200 (domyślną serwera) i zapisało ją jako JAWNE");
Console.WriteLine("  przypięcie tej bazy. Jeżeli 0 nie jest zapisywalne, powrót do „dziedzicz\" jest niemożliwy —");
Console.WriteLine("  a to zmienia odpowiedź na pytanie, czy tę pozycję wolno udostępnić do edycji.");

try
{
    await new FbConfiguration(Svc()).SetPageBuffersAsync(0);
    Console.WriteLine("    zapis SetPageBuffersAsync(0)                       : OK (bez wyjątku)");
    Console.WriteLine($"    odczyt po pełnym zwolnieniu                        : {await ReadOnFresh(pbSql)}");
    Console.WriteLine("    (jeżeli wróciło 51200 = domyślna serwera, to 0 znaczy „dziedzicz\" i jest odwracalne)");
}
catch (Exception ex)
{
    Console.WriteLine($"    zapis SetPageBuffersAsync(0)                       : ⛔ {Short(ex)}");
    Console.WriteLine("    ⇒ powrót do „dziedzicz domyślną serwera\" NIE jest dostępny przez to API");
}

// ═════ STAN KOŃCOWY ══════════════════════════════════════════════════════════════════════════════════
Section("STAN KOŃCOWY bazy scratch (dowód, że nic nie zostało zmienione na trwałe)");
await using (var c = new FbConnection(Db().ToString()))
{
    await c.OpenAsync();
    await using var cmd = new FbCommand(
        "SELECT MON$SWEEP_INTERVAL, MON$FORCED_WRITES, MON$PAGE_BUFFERS, MON$RESERVE_SPACE, " +
        "MON$SQL_DIALECT, MON$READ_ONLY FROM MON$DATABASE", c);
    await using var r = await cmd.ExecuteReaderAsync();
    if (await r.ReadAsync())
    {
        for (var i = 0; i < r.FieldCount; i++)
        {
            Console.WriteLine($"    {r.GetName(i),-24} = {r.GetValue(i)}");
        }
    }
}

Console.WriteLine();
Console.WriteLine("KONIEC. Sonda nie zmieniła niczego w Lab/ ani w aplikacji.");
