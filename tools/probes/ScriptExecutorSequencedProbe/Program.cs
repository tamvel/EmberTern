using System.Globalization;
using System.Text;
using EmberTern.Core.Connections;
using EmberTern.Core.Scripting;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;

// DEVELOPER VERIFICATION TOOL — NOT PART OF THE PRODUCT. See tools/probes/README.md.
//
// Script Executor Rewrite — Step 4 seam B (Sequenced execution loop). Drives the REAL
// FirebirdScriptExecutor against a THROWAWAY scratch DB (never the lab) to prove sim==reality:
// a mixed migration runs under Sequenced, the old mode still fails at #213, and a mid-script
// failure keeps earlier segments committed while rolling back only the failing one.
//
//   $env:ET_LAB_PWD = "<local dev SYSDBA password>"
//   dotnet run --project tools\probes\ScriptExecutorSequencedProbe

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

var pwd = Environment.GetEnvironmentVariable("ET_LAB_PWD");
if (string.IsNullOrWhiteSpace(pwd))
{
    Console.WriteLine("Set ET_LAB_PWD to the local dev SYSDBA password.");
    return 2;
}

int failures = 0;
void Pass(string what, string detail = "") => Console.WriteLine($"  PASS  {what}{(detail.Length > 0 ? "  — " + detail : "")}");
void Fail(string what, string detail) { failures++; Console.WriteLine($"  FAIL  {what}  — {detail}"); }
void Head(string t) => Console.WriteLine($"\n=== {t} ===");

Directory.CreateDirectory(@"C:\Temp");
string scratchPath = Path.Combine(@"C:\Temp", $"et_seq_probe_{Guid.NewGuid():N}.fdb");

FbConnectionStringBuilder Csb(string db) => new()
{
    Database = db, DataSource = "localhost", Port = 3050, UserID = "SYSDBA",
    Password = pwd, Charset = "WIN1250", Dialect = 3, ServerType = FbServerType.Default, Pooling = false,
};

Console.WriteLine($"Creating scratch DB: {scratchPath}");
FbConnection.CreateDatabase(Csb(scratchPath).ConnectionString, overwrite: true);

// A fresh direct connection for verification queries (committed data is visible cross-attachment).
async Task<long> ScalarAsync(string sql)
{
    await using var conn = new FbConnection(Csb(scratchPath).ConnectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    var result = await cmd.ExecuteScalarAsync();
    return result is null or DBNull ? 0 : Convert.ToInt64(result, CultureInfo.InvariantCulture);
}

Task<long> TableExistsAsync(string name) =>
    ScalarAsync($"SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = '{name}'");
Task<long> IndexExistsAsync(string name) =>
    ScalarAsync($"SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = '{name}'");

async Task<ScriptRunOutcome> RunAsync(string script, ScriptTransactionMode mode)
{
    var profile = new ConnectionProfile
    {
        Name = "scratch", Host = "localhost", Port = 3050, DatabasePath = scratchPath,
        Username = "SYSDBA", Password = pwd, Charset = "WIN1250", Dialect = 3,
    };
    var service = new FirebirdConnectionService();
    try
    {
        await service.ConnectAsync(profile);
        var tx = new TransactionService(service);
        var exec = new FirebirdScriptExecutor(service, tx);
        var statements = new FirebirdScriptParser().Parse(script);
        return await exec.RunAsync(statements, mode, stopOnError: true, progress: null);
    }
    finally
    {
        await service.DisconnectAsync();
        service.Dispose();
    }
}

try
{
    // ── Case A — mixed CREATE+INSERT migration under Sequenced runs end-to-end (#213 fixed) ────────
    Head("A · Sequenced mixed migration (create → insert → index → insert)");
    const string migration = @"
CREATE TABLE SEQ_PROBE (ID INTEGER NOT NULL PRIMARY KEY, NOTE VARCHAR(50));
INSERT INTO SEQ_PROBE (ID, NOTE) VALUES (1, 'alpha');
INSERT INTO SEQ_PROBE (ID, NOTE) VALUES (2, 'beta');
CREATE INDEX IX_SEQ_PROBE_NOTE ON SEQ_PROBE (NOTE);
INSERT INTO SEQ_PROBE (ID, NOTE) VALUES (3, 'gamma');
";
    var a = await RunAsync(migration, ScriptTransactionMode.Sequenced);
    if (a.AnyFailed) Fail("A migration succeeds", "a statement failed: " +
        string.Join(" | ", a.Results.Where(r => !r.Success).Select(r => r.Error)));
    else Pass("A migration succeeds", $"{a.Results.Count} statements, none failed");
    if (!a.TransactionLeftOpen) Pass("A leaves nothing open"); else Fail("A leaves nothing open", "tx left open");
    var rows = await ScalarAsync("SELECT COUNT(*) FROM SEQ_PROBE");
    if (rows == 3) Pass("A data persisted", "3 rows committed"); else Fail("A data persisted", $"expected 3, got {rows}");
    if (await IndexExistsAsync("IX_SEQ_PROBE_NOTE") == 1) Pass("A index persisted");
    else Fail("A index persisted", "IX_SEQ_PROBE_NOTE missing");

    // ── Case B — the SAME migration under AutoCommitOnSuccess still fails at #213 ───────────────────
    Head("B · AutoCommitOnSuccess mixed migration still hits #213 (contrast)");
    const string migrationAc = @"
CREATE TABLE SEQ_AC (ID INTEGER NOT NULL PRIMARY KEY);
INSERT INTO SEQ_AC (ID) VALUES (1);
";
    var b = await RunAsync(migrationAc, ScriptTransactionMode.AutoCommitOnSuccess);
    if (b.AnyFailed) Pass("B AutoCommit fails on the INSERT",
        b.Results.FirstOrDefault(r => !r.Success)?.Error?.Split('\n')[0] ?? "");
    else Fail("B AutoCommit fails on the INSERT", "expected the INSERT to fail (#213), it did not");
    if (await TableExistsAsync("SEQ_AC") == 0) Pass("B whole run rolled back", "SEQ_AC does not exist");
    else Fail("B whole run rolled back", "SEQ_AC exists — the failed AutoCommit run did not roll back");

    // ── Case C — Sequenced partial-commit: earlier segments stay, the failing one rolls back ───────
    Head("C · Sequenced mid-script failure (committed segments persist, failing segment rolls back)");
    const string partial = @"
CREATE TABLE SEQ_PART (ID INTEGER NOT NULL PRIMARY KEY, NOTE VARCHAR(20));
INSERT INTO SEQ_PART (ID, NOTE) VALUES (1, 'ok');
CREATE INDEX IX_SEQ_PART ON SEQ_PART (NOTE);
INSERT INTO SEQ_PART (ID, NOTE) VALUES (1, 'dup');
";
    var c = await RunAsync(partial, ScriptTransactionMode.Sequenced);
    if (c.AnyFailed && !c.Results[^1].Success) Pass("C last statement fails (PK dup)",
        c.Results[^1].Error?.Split('\n')[0] ?? "");
    else Fail("C last statement fails (PK dup)", "expected the duplicate INSERT to fail");
    if (c.Results.Take(3).All(r => r.Success)) Pass("C earlier statements succeeded");
    else Fail("C earlier statements succeeded", "an earlier statement failed unexpectedly");
    if (await TableExistsAsync("SEQ_PART") == 1) Pass("C table committed"); else Fail("C table committed", "SEQ_PART missing");
    if (await IndexExistsAsync("IX_SEQ_PART") == 1) Pass("C index committed"); else Fail("C index committed", "index missing");
    var partRows = await ScalarAsync("SELECT COUNT(*) FROM SEQ_PART");
    if (partRows == 1) Pass("C only the committed row remains", "1 row (the failing segment rolled back)");
    else Fail("C only the committed row remains", $"expected 1, got {partRows}");
}
catch (Exception ex)
{
    Fail("probe", ex.Message);
}
finally
{
    try { File.Delete(scratchPath); Console.WriteLine($"\nDeleted scratch DB: {scratchPath}"); }
    catch (Exception ex) { Console.WriteLine($"\n(could not delete scratch DB — throwaway, ignore) {ex.Message}"); }
}

Console.WriteLine($"\n{(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)")}");
return failures == 0 ? 0 : 1;
