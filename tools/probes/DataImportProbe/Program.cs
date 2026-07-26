// Data Import — etap I4 live verification. See DataImportProbe.csproj for what this is and why.
//
//   dotnet run --project tools/probes/DataImportProbe
//
// Requires the local FB5 DefaultInstance on localhost:3050 and Lab/EmberTern_Lab.fdb.

using System.Globalization;
using System.Text;
using EmberTern.Core.Connections;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;

var labPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Lab", "EmberTern_Lab.fdb"));

if (!File.Exists(labPath))
{
    Console.Error.WriteLine($"Lab database not found at {labPath}");
    return 2;
}

var profile = new ConnectionProfile
{
    Name = "I4 probe",
    Host = "localhost",
    Port = 3050,
    DatabasePath = labPath,
    Username = "SYSDBA",
    Password = "masterkey",
    Charset = "WIN1250",
    Dialect = 3,
};

int passed = 0, failed = 0;

void Pass(string tag, string detail)
{
    passed++;
    Console.WriteLine($"  PASS  {tag,-42} {detail}");
}

void Fail(string tag, string detail)
{
    failed++;
    Console.WriteLine($"  FAIL  {tag,-42} {detail}");
}

void Section(string title)
    => Console.WriteLine($"{Environment.NewLine}── {title} {new string('─', Math.Max(0, 70 - title.Length))}");

Console.WriteLine($"Data Import — I4 live verification against {labPath}");

var connectionService = new FirebirdConnectionService();
await connectionService.ConnectAsync(profile);
var transactionService = new TransactionService(connectionService);
var lane = new MetadataLane(connectionService, transactionService);
var metadataReader = new FirebirdMetadataReader(connectionService, lane);
var targetReader = new FirebirdImportTargetReader(metadataReader, lane);

Console.WriteLine($"Connected. Server: {connectionService.RequireOpenConnection().ServerVersion}");

// ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────

async Task ExecAsync(string sql)
{
    var connection = connectionService.RequireOpenConnection();
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    cmd.Transaction = transactionService.ActiveTransaction;
    await cmd.ExecuteNonQueryAsync();
}

async Task<long> CountAsync(string table)
{
    var connection = connectionService.RequireOpenConnection();
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
    cmd.Transaction = transactionService.ActiveTransaction;
    return Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
}

/// Runs one import through the REAL pipeline and rolls back, so each case starts clean.
async Task<ImportOutcome> ImportAsync(
    string table,
    string csv,
    string[] columns,
    ImportErrorPolicy policy = ImportErrorPolicy.SkipInvalidRows,
    int batchSize = ImportConfiguration.DefaultBatchSize,
    Func<Task>? seed = null)
{
    await transactionService.BeginTransactionAsync();
    await ExecAsync($"DELETE FROM {table}");
    if (seed is not null) await seed();

    var target = await targetReader.ReadTargetAsync(table)
        ?? throw new InvalidOperationException($"{table} not found");

    var configuration = new ImportConfiguration
    {
        Source = SourceDescriptor.Clipboard(),
        Target = TargetDescriptor.Existing(table),
        ErrorPolicy = policy,
        BatchSize = batchSize,
        Mapping = columns.Select((c, i) => new ColumnMapping
        {
            TargetColumnName = c,
            SourceFieldName = c,
            SourceFieldIndex = i,
        }).ToArray(),
    };

    var writer = new FirebirdImportWriter(transactionService, policy);
    return await ImportPipeline.RunAsync(
        configuration,
        target,
        new DelimitedTextImportProvider(),
        new TextImportSource(csv),
        writer,
        ImportCharsetGuard.Strict(profile.Charset));
}

string KindsOf(ImportOutcome outcome)
    => string.Join(", ", outcome.Errors.Select(e => $"row {e.SourceRowNumber}={e.Kind}"));

// ── (E) Error classes: right kind, right SOURCE row ─────────────────────────────────────────────────────

Section("(E) Error classification from the GDS vector, and row attribution");

async Task ErrorCaseAsync(
    string tag, string table, string csv, string[] columns, ImportErrorKind expectedKind,
    int expectedRow, Func<Task>? seed = null)
{
    try
    {
        var outcome = await ImportAsync(table, csv, columns, ImportErrorPolicy.SkipInvalidRows, seed: seed);
        var error = outcome.Errors.FirstOrDefault();

        if (error is null) Fail(tag, $"expected {expectedKind}, got no error at all ({outcome.RowsWritten} written)");
        else if (error.Kind != expectedKind) Fail(tag, $"expected {expectedKind}, got {error.Kind} — {error.ServerMessage}");
        else if (error.SourceRowNumber != expectedRow) Fail(tag, $"{expectedKind} OK but row {error.SourceRowNumber} != {expectedRow}");
        else Pass(tag, $"{expectedKind} at source row {error.SourceRowNumber}" +
                       (error.Limit is not null ? $" (limit {error.Limit}, actual {error.ActualLength})" : ""));
    }
    catch (Exception ex)
    {
        Fail(tag, $"threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
    }
    finally
    {
        await transactionService.RollbackAsync();
    }
}

var cols = new[] { "ID", "CODE", "NAME", "QTY", "PRICE" };
var srvCols = new[] { "ID", "MODE", "CODE", "NAME", "QTY" };

// The bad row is the THIRD data row every time, i.e. source row 4 with a header — deliberately not row 1,
// so an off-by-one or a batch-index leak cannot pass by coincidence.

await ErrorCaseAsync(
    "E1  uniqueness (PK constraint)", "IMP_TARGET",
    "ID;CODE;NAME;QTY;PRICE\n1;A;n;1;1,00\n2;B;n;1;1,00\n1;C;n;1;1,00\n",
    cols, ImportErrorKind.ServerUniqueViolation, 4);

// ⭐ A standalone CREATE UNIQUE INDEX leads with a DIFFERENT GDS code (335544349) from the constraint form
// above (335544665). I0 measured only the constraint, so this case is the one that found the gap.
await ErrorCaseAsync(
    "E2  uniqueness (UNIQUE index) ⭐", "IMP_TARGET",
    "ID;CODE;NAME;QTY;PRICE\n1;A;n;1;1,00\n2;B;n;1;1,00\n3;A;n;1;1,00\n",
    cols, ImportErrorKind.ServerUniqueViolation, 4);

await ErrorCaseAsync(
    "E3  CHECK", "IMP_TARGET",
    "ID;CODE;NAME;QTY;PRICE\n1;A;n;1;1,00\n2;B;n;1;1,00\n3;C;n;-5;1,00\n",
    cols, ImportErrorKind.ServerCheckViolation, 4);

await ErrorCaseAsync(
    "E4  foreign key", "IMP_CHILD",
    "ID;PARENT_ID\n1;1\n2;1\n3;999\n",
    new[] { "ID", "PARENT_ID" }, ImportErrorKind.ServerForeignKeyViolation, 4,
    seed: () => ExecAsync("DELETE FROM IMP_CHILD"));

// ⭐⭐ The three failures that share leading GDS 335544321 and separate ONLY on a later element of the
// vector. They are reached through IMP_SRV's BEFORE INSERT trigger, because the client validates NOT NULL,
// length and numeric range BEFORE the round trip (see the E8-E10 block) — so without a trigger manufacturing
// them inside the engine, these mapper branches could not be exercised against a live server at all.
await ErrorCaseAsync(
    "E5  server string truncation ⭐", "IMP_SRV",
    "ID;MODE;CODE;NAME;QTY\n1;OK;A;n;1\n2;OK;B;n;1\n3;TRUNC;C;n;1\n",
    srvCols, ImportErrorKind.ServerStringTruncation, 4);

await ErrorCaseAsync(
    "E6  server numeric overflow ⭐", "IMP_SRV",
    "ID;MODE;CODE;NAME;QTY\n1;OK;A;n;1\n2;OK;B;n;1\n3;OVER;C;n;2\n",
    srvCols, ImportErrorKind.ServerNumericOverflow, 4);

await ErrorCaseAsync(
    "E7  server NOT NULL", "IMP_SRV",
    "ID;MODE;CODE;NAME;QTY\n1;OK;A;n;1\n2;OK;B;n;1\n3;NULL;C;n;1\n",
    srvCols, ImportErrorKind.ServerNullViolation, 4);

// ── The client guards fire FIRST, which is the design (§0) ───────────────────────────────────────────────
// These three would each be a server refusal if they got that far. They do not, and that is better: the row
// never leaves the machine, the message names the column and the original value, and the round trip is saved.
// Pinned so nobody later "fixes" the client checks away and calls the server messages an improvement.

await ErrorCaseAsync(
    "E8  NOT NULL caught client-side", "IMP_TARGET",
    "ID;CODE;NAME;QTY;PRICE\n1;A;n;1;1,00\n2;B;n;1;1,00\n3;;n;1;1,00\n",
    cols, ImportErrorKind.NullNotAllowed, 4);

await ErrorCaseAsync(
    "E9  too long caught client-side", "IMP_TARGET",
    "ID;CODE;NAME;QTY;PRICE\n1;A;n;1;1,00\n2;B;n;1;1,00\n3;WAY-TOO-LONG-CODE;n;1;1,00\n",
    cols, ImportErrorKind.ValueTooLong, 4);

await ErrorCaseAsync(
    "E10 out of range caught client-side", "IMP_TARGET",
    "ID;CODE;NAME;QTY;PRICE\n1;A;n;1;1,00\n2;B;n;1;1,00\n3;C;n;1;99999999999,00\n",
    cols, ImportErrorKind.ValueOutOfRange, 4);

// ── (B) Batch behaviour matches the I0 measurements ──────────────────────────────────────────────────────

Section("(B) FbBatchCommand behaviour vs the I0 measurements");

// B1 — SkipInvalidRows (MultiError=true): good rows continue past the bad one, counts are exact.
try
{
    var outcome = await ImportAsync(
        "IMP_TARGET",
        "ID;CODE;NAME;QTY;PRICE\n1;A;n;1;1,00\n2;;n;1;1,00\n3;C;n;1;1,00\n4;D;n;1;1,00\n",
        cols, ImportErrorPolicy.SkipInvalidRows);

    var persisted = await CountAsync("IMP_TARGET");
    if (outcome.RowsWritten == 3 && outcome.RowsFailed == 1 && persisted == 3)
        Pass("B1  SkipInvalidRows", $"written={outcome.RowsWritten} failed={outcome.RowsFailed} COUNT(*)={persisted} — good rows continue past the bad one");
    else
        Fail("B1  SkipInvalidRows", $"written={outcome.RowsWritten} failed={outcome.RowsFailed} COUNT(*)={persisted} (expected 3/1/3) — {KindsOf(outcome)}");
}
catch (Exception ex) { Fail("B1  SkipInvalidRows", $"threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
finally { await transactionService.RollbackAsync(); }

// B2 — StopOnFirstError (MultiError=false): the batch stops AT the offending row.
try
{
    var outcome = await ImportAsync(
        "IMP_TARGET",
        "ID;CODE;NAME;QTY;PRICE\n1;A;n;1;1,00\n2;;n;1;1,00\n3;C;n;1;1,00\n4;D;n;1;1,00\n",
        cols, ImportErrorPolicy.StopOnFirstError);

    var error = outcome.Errors.FirstOrDefault();
    if (outcome.RowsWritten == 1 && outcome.RowsFailed == 1 && error?.SourceRowNumber == 3)
        Pass("B2  StopOnFirstError", $"stopped AT the bad row: written={outcome.RowsWritten} failed at source row {error.SourceRowNumber}");
    else
        Fail("B2  StopOnFirstError", $"written={outcome.RowsWritten} failed={outcome.RowsFailed} — {KindsOf(outcome)}");
}
catch (Exception ex) { Fail("B2  StopOnFirstError", $"threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
finally { await transactionService.RollbackAsync(); }

// ⭐ B3 — the row number must survive a BATCH BOUNDARY. With batchSize 2 the bad row is position 0 of the
// third batch; a pipeline leaking the batch index would say "row 0"/"row 1" instead of source row 6.
try
{
    var csv = new StringBuilder("ID;CODE;NAME;QTY;PRICE\n");
    for (var i = 1; i <= 6; i++)
        csv.Append(CultureInfo.InvariantCulture, $"{i};C{i};n;1;1,00\n");
    // Row 6 of the file (5th data row) duplicates ID 1.
    csv.Replace("5;C5;n;1;1,00", "1;C5;n;1;1,00");

    var outcome = await ImportAsync("IMP_TARGET", csv.ToString(), cols, ImportErrorPolicy.SkipInvalidRows, batchSize: 2);
    var error = outcome.Errors.FirstOrDefault();

    if (error?.SourceRowNumber == 6 && error.Kind == ImportErrorKind.ServerUniqueViolation)
        Pass("B3  row number across batches ⭐", $"source row {error.SourceRowNumber} (batch size 2 ⇒ position 0 of batch 3)");
    else
        Fail("B3  row number across batches ⭐", $"{KindsOf(outcome)} (expected row 6 = ServerUniqueViolation)");
}
catch (Exception ex) { Fail("B3  row number across batches ⭐", $"threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
finally { await transactionService.RollbackAsync(); }

// B4 — 10 000 rows: the counts must agree with the server's own COUNT(*).
try
{
    var csv = new StringBuilder("ID;CODE;NAME;QTY;PRICE\n");
    for (var i = 1; i <= 10_000; i++)
        csv.Append(CultureInfo.InvariantCulture, $"{i};C{i};name {i};1;1,50\n");

    var started = DateTime.UtcNow;
    var outcome = await ImportAsync("IMP_TARGET", csv.ToString(), cols);
    var elapsed = DateTime.UtcNow - started;
    var persisted = await CountAsync("IMP_TARGET");

    if (outcome.RowsWritten == 10_000 && outcome.RowsFailed == 0 && persisted == 10_000)
        Pass("B4  10 000 rows", $"written={outcome.RowsWritten} COUNT(*)={persisted} in {elapsed.TotalSeconds:F2}s ({10_000 / Math.Max(elapsed.TotalSeconds, 0.001):N0} rows/s)");
    else
        Fail("B4  10 000 rows", $"written={outcome.RowsWritten} failed={outcome.RowsFailed} COUNT(*)={persisted}");
}
catch (Exception ex) { Fail("B4  10 000 rows", $"threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
finally { await transactionService.RollbackAsync(); }

// ── (W) The writer's own obligations ─────────────────────────────────────────────────────────────────────

Section("(W) Writer obligations: identity override, triggers, transaction discipline");

// W1 — OVERRIDING SYSTEM VALUE: without it Firebird refuses an INSERT naming a GENERATED ALWAYS column.
try
{
    var outcome = await ImportAsync(
        "IMP_IDENTITY", "ID;NOTE\n1;first\n2;second\n", new[] { "ID", "NOTE" });

    if (outcome.RowsWritten == 2 && outcome.RowsFailed == 0)
        Pass("W1  OVERRIDING SYSTEM VALUE", "a GENERATED ALWAYS identity accepted an explicit value");
    else
        Fail("W1  OVERRIDING SYSTEM VALUE", $"written={outcome.RowsWritten} failed={outcome.RowsFailed} — {KindsOf(outcome)}");
}
catch (Exception ex) { Fail("W1  OVERRIDING SYSTEM VALUE", $"threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
finally { await transactionService.RollbackAsync(); }

// ⭐ W2 — the multi-action trigger must be FOUND. RDB$TRIGGER_TYPE is bit-encoded (this one reads 17, not 1),
// so a reader testing `type = 1` would report "no triggers" on a table that rewrites every value it stores.
try
{
    var target = await targetReader.ReadTargetAsync("IMP_TRIGGERED");
    var triggers = target?.BeforeInsertTriggers ?? Array.Empty<string>();

    if (triggers.Contains("IMP_TRG_BIU"))
        Pass("W2  multi-action BEFORE trigger ⭐", $"found [{string.Join(", ", triggers)}] on a BEFORE INSERT OR UPDATE trigger");
    else
        Fail("W2  multi-action BEFORE trigger ⭐", $"found [{string.Join(", ", triggers)}] — expected IMP_TRG_BIU");
}
catch (Exception ex) { Fail("W2  multi-action BEFORE trigger ⭐", $"threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }

// W3 — the target reader reports a missing table as null, not as an exception (§4.8.5: a stale profile is
// an ordinary situation the readiness strip explains).
try
{
    var missing = await targetReader.ReadTargetAsync("NO_SUCH_TABLE_XYZ");
    if (missing is null) Pass("W3  missing table", "reported as null ⇒ TargetNotFound, not an exception");
    else Fail("W3  missing table", $"returned a target with {missing.Columns.Count} columns");
}
catch (Exception ex) { Fail("W3  missing table", $"threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }

// ⭐ W4 — hard rule #3: the writer NEVER commits. After a run the transaction is still open and a Rollback
// must remove every row it wrote.
try
{
    var outcome = await ImportAsync("IMP_TARGET", "ID;CODE;NAME;QTY;PRICE\n1;A;n;1;1,00\n", cols);
    var duringRun = await CountAsync("IMP_TARGET");
    var stillOpen = transactionService.IsActive && outcome.TransactionLeftOpen;

    await transactionService.RollbackAsync();
    await transactionService.BeginTransactionAsync();
    var afterRollback = await CountAsync("IMP_TARGET");
    await transactionService.RollbackAsync();

    if (stillOpen && duringRun == 1 && afterRollback == 0)
        Pass("W4  never auto-commits ⭐", "transaction left OPEN; Rollback removed the imported row");
    else
        Fail("W4  never auto-commits ⭐", $"open={stillOpen} duringRun={duringRun} afterRollback={afterRollback}");
}
catch (Exception ex) { Fail("W4  never auto-commits ⭐", $"threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }

// ── (C) Charset — the client guard fires before the server ever sees the value ───────────────────────────

Section("(C) Connection charset (design R1)");

try
{
    // Cyrillic over a WIN1250 connection: I0 measured that the driver writes '?' with NO error. The client
    // guard must refuse the row instead — that is the whole reason validation is a §0 requirement.
    var outcome = await ImportAsync("IMP_TARGET", "ID;CODE;NAME;QTY;PRICE\n1;A;Ж;1;1,00\n", cols);
    var error = outcome.Errors.FirstOrDefault();

    if (error?.Kind == ImportErrorKind.NotRepresentableInConnectionCharset)
        Pass("C1  unrepresentable character ⭐", $"refused CLIENT-SIDE at source row {error.SourceRowNumber} — never written as '?'");
    else
        Fail("C1  unrepresentable character ⭐", $"written={outcome.RowsWritten} — {KindsOf(outcome)}");
}
catch (Exception ex) { Fail("C1  unrepresentable character ⭐", $"threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
finally { if (transactionService.IsActive) await transactionService.RollbackAsync(); }

try
{
    var outcome = await ImportAsync("IMP_TARGET", "ID;CODE;NAME;QTY;PRICE\n1;A;zażółć;1;1,00\n", cols);
    if (outcome.RowsWritten == 1)
        Pass("C2  Polish text in WIN1250", "accepted — the guard does not fire on everyday data");
    else
        Fail("C2  Polish text in WIN1250", $"written={outcome.RowsWritten} — {KindsOf(outcome)}");
}
catch (Exception ex) { Fail("C2  Polish text in WIN1250", $"threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
finally { if (transactionService.IsActive) await transactionService.RollbackAsync(); }

// ── Verdict ─────────────────────────────────────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine(failed == 0
    ? $"ALL PASS — {passed} checks"
    : $"{failed} FAILED, {passed} passed");

await connectionService.DisconnectAsync();
return failed == 0 ? 0 : 1;
