// Can Data Import have its OWN working transaction? — the measurement behind the answer.
//
//   dotnet run --project tools/probes/ImportTransactionIndependenceProbe
//
// Requires the local FB5 DefaultInstance on localhost:3050 and Lab/EmberTern_Lab.fdb.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using FirebirdSql.Data.FirebirdClient;

// WIN1250 needs the code-pages provider registered BEFORE any OpenAsync — FirebirdConnectionService does this
// in its static ctor, and this probe deliberately talks to the raw driver instead, so it must do it itself.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var labPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Lab", "EmberTern_Lab.fdb"));

if (!File.Exists(labPath))
{
    Console.Error.WriteLine($"Lab database not found at {labPath}");
    return 2;
}

var csb = new FbConnectionStringBuilder
{
    DataSource = "localhost",
    Port = 3050,
    Database = labPath,
    UserID = "SYSDBA",
    Password = "masterkey",
    Charset = "WIN1250",
    Dialect = 3,
    Pooling = false,
};
var connectionString = csb.ToString();

int passed = 0, failed = 0;
void Result(bool ok, string tag, string detail)
{
    if (ok) passed++; else failed++;
    Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {tag,-44} {detail}");
}
void Note(string tag, string detail) => Console.WriteLine($"  ····  {tag,-44} {detail}");
void Section(string t) => Console.WriteLine($"{Environment.NewLine}── {t} {new string('─', Math.Max(0, 66 - t.Length))}");

static async Task<FbConnection> OpenAsync(string cs)
{
    var c = new FbConnection(cs);
    await c.OpenAsync();
    return c;
}

static async Task ExecAsync(FbConnection c, FbTransaction? tx, string sql)
{
    await using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    cmd.Transaction = tx;
    await cmd.ExecuteNonQueryAsync();
}

static async Task<long> CountAsync(FbConnection c, FbTransaction? tx)
{
    await using var cmd = c.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM IMP_TARGET";
    cmd.Transaction = tx;
    return Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
}

// The TPB EmberTern actually uses for the user's working transaction: write + read committed + NOWAIT.
static FbTransactionOptions WorkingTx() => new()
{
    TransactionBehavior = FbTransactionBehavior.Write
                          | FbTransactionBehavior.ReadCommitted
                          | FbTransactionBehavior.RecVersion
                          | FbTransactionBehavior.NoWait,
};

Console.WriteLine($"Two independent working transactions? — measured against {labPath}");

await using (var setup = await OpenAsync(connectionString))
{
    Console.WriteLine($"Server: {setup.ServerVersion}");
    await ExecAsync(setup, null, "DELETE FROM IMP_TARGET");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("1 — ONE attachment, two transactions: what does the driver do?");
{
    await using var a = await OpenAsync(connectionString);
    var t1 = a.BeginTransaction(WorkingTx());
    try
    {
        var t2 = a.BeginTransaction(WorkingTx());
        Result(false, "1a second BeginTransaction", "the driver ALLOWED it — model assumption is wrong");
        t2.Rollback();
    }
    catch (Exception ex)
    {
        Result(true, "1a second BeginTransaction refused", ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
    }
    t1.Rollback();
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("2 — TWO attachments: two genuinely independent transactions?");
{
    await using var a = await OpenAsync(connectionString);
    await using var b = await OpenAsync(connectionString);

    var ta = a.BeginTransaction(WorkingTx());
    var tb = b.BeginTransaction(WorkingTx());

    await ExecAsync(a, ta, "INSERT INTO IMP_TARGET (ID, CODE, NAME) VALUES (1, 'A1', 'from A')");
    await ExecAsync(b, tb, "INSERT INTO IMP_TARGET (ID, CODE, NAME) VALUES (2, 'B1', 'from B')");

    Result(true, "2a both wrote concurrently", "different rows, neither blocked");

    // A commits, B rolls back — the whole point: one decision must not decide the other.
    ta.Commit();
    tb.Rollback();

    await using var witness = await OpenAsync(connectionString);
    var total = await CountAsync(witness, null);
    Result(total == 1, "2b independent commit / rollback", $"{total} row(s) survived — expected 1 (A's)");

    await ExecAsync(witness, null, "DELETE FROM IMP_TARGET");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("3 — isolation: is A's uncommitted work invisible to B?");
{
    await using var a = await OpenAsync(connectionString);
    await using var b = await OpenAsync(connectionString);

    var ta = a.BeginTransaction(WorkingTx());
    var tb = b.BeginTransaction(WorkingTx());

    await ExecAsync(a, ta, "INSERT INTO IMP_TARGET (ID, CODE, NAME) VALUES (10, 'HID', 'uncommitted')");

    var seenByA = await CountAsync(a, ta);
    var seenByB = await CountAsync(b, tb);
    Result(seenByA == 1 && seenByB == 0, "3a uncommitted rows stay private",
        $"A sees {seenByA}, B sees {seenByB}");

    ta.Commit();
    var seenByBAfter = await CountAsync(b, tb);
    Result(seenByBAfter == 1, "3b read committed sees it after commit", $"B now sees {seenByBAfter}");

    tb.Rollback();
    await using var witness = await OpenAsync(connectionString);
    await ExecAsync(witness, null, "DELETE FROM IMP_TARGET");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("4 — the REAL cost: two transactions touching the SAME row");
{
    await using var seed = await OpenAsync(connectionString);
    await ExecAsync(seed, null, "INSERT INTO IMP_TARGET (ID, CODE, NAME) VALUES (99, 'SHARED', 'seed')");

    await using var a = await OpenAsync(connectionString);
    await using var b = await OpenAsync(connectionString);

    var ta = a.BeginTransaction(WorkingTx());
    var tb = b.BeginTransaction(WorkingTx());

    await ExecAsync(a, ta, "UPDATE IMP_TARGET SET NAME = 'A wins' WHERE ID = 99");

    var clock = Stopwatch.StartNew();
    try
    {
        await ExecAsync(b, tb, "UPDATE IMP_TARGET SET NAME = 'B tries' WHERE ID = 99");
        clock.Stop();
        Result(false, "4a same-row write under NOWAIT", $"B succeeded in {clock.ElapsedMilliseconds} ms — no conflict?");
    }
    catch (FbException ex)
    {
        clock.Stop();
        Result(true, "4a same-row write conflicts immediately",
            $"{clock.ElapsedMilliseconds} ms, SQLSTATE {ex.SQLSTATE}, GDS {ex.ErrorCode}");
        Note("4a message", ex.Message.Split('\n')[0]);
    }

    // And the same INSERT collision on a unique key — the shape an import would actually hit.
    try
    {
        await ExecAsync(b, tb, "INSERT INTO IMP_TARGET (ID, CODE, NAME) VALUES (99, 'SHARED2', 'dup')");
        Result(false, "4b duplicate PK against an uncommitted row", "B succeeded — unexpected");
    }
    catch (FbException ex)
    {
        Result(true, "4b duplicate PK against an uncommitted row",
            $"SQLSTATE {ex.SQLSTATE}, GDS {ex.ErrorCode}");
    }

    ta.Rollback();
    tb.Rollback();

    await using var witness = await OpenAsync(connectionString);
    await ExecAsync(witness, null, "DELETE FROM IMP_TARGET");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("5 — how many attachments does one app realistically hold?");
{
    // EmberTern already opens Data + Metadata + Ddl per profile, plus one per live debug session.
    // An import lane would be a fifth. Confirm the server is not close to a limit at that scale.
    var held = new List<FbConnection>();
    try
    {
        for (var i = 0; i < 8; i++) held.Add(await OpenAsync(connectionString));
        Result(true, "5a eight concurrent attachments", "opened without refusal");
    }
    catch (Exception ex)
    {
        Result(false, "5a eight concurrent attachments", ex.Message.Split('\n')[0]);
    }
    finally
    {
        foreach (var c in held) await c.DisposeAsync();
    }
}

await using (var cleanup = await OpenAsync(connectionString))
{
    await ExecAsync(cleanup, null, "DELETE FROM IMP_TARGET");
}

Console.WriteLine();
Console.WriteLine(failed == 0 ? $"ALL PASS — {passed} check(s)." : $"{failed} FAILED, {passed} passed.");
return failed == 0 ? 0 : 1;
