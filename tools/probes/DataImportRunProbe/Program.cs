// Data Import — etap I7 live verification. See DataImportRunProbe.csproj for what this is and why.
//
//   dotnet run --project tools/probes/DataImportRunProbe
//
// Requires the local FB5 DefaultInstance on localhost:3050 and Lab/EmberTern_Lab.fdb.

using System.Globalization;
using EmberTern.Core.Connections;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using EmberTern.Firebird;

var labPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Lab", "EmberTern_Lab.fdb"));

if (!File.Exists(labPath))
{
    Console.Error.WriteLine($"Lab database not found at {labPath}");
    return 2;
}

var profile = new ConnectionProfile
{
    Name = "I7 probe",
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
    Console.WriteLine($"  PASS  {tag,-46} {detail}");
}

void Fail(string tag, string detail)
{
    failed++;
    Console.WriteLine($"  FAIL  {tag,-46} {detail}");
}

void Section(string title)
    => Console.WriteLine($"{Environment.NewLine}── {title} {new string('─', Math.Max(0, 70 - title.Length))}");

Console.WriteLine($"Data Import — I7 live verification against {labPath}");

var connectionService = new FirebirdConnectionService();
await connectionService.ConnectAsync(profile);
var transactionService = new TransactionService(connectionService);
var lane = new MetadataLane(connectionService, transactionService);
var metadataReader = new FirebirdMetadataReader(connectionService, lane);
var targetReader = new FirebirdImportTargetReader(metadataReader, lane);
var preparer = new FirebirdImportTargetPreparer(transactionService);

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

// The independent witness: counted OUTSIDE any import machinery, in its own committed transaction, so it
// answers "what is actually in the table" rather than "what our code believes".
async Task<long> CountCommittedAsync()
{
    if (transactionService.IsActive) throw new InvalidOperationException("Count with no transaction open.");

    await transactionService.BeginTransactionAsync();
    var connection = connectionService.RequireOpenConnection();
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM IMP_TARGET";
    cmd.Transaction = transactionService.ActiveTransaction;
    var scalar = await cmd.ExecuteScalarAsync();
    await transactionService.CommitAsync();
    return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
}

async Task ResetAsync()
{
    if (!transactionService.IsActive) await transactionService.BeginTransactionAsync();
    await ExecAsync("DELETE FROM IMP_TARGET");
    await transactionService.CommitAsync();
}

async Task SeedAsync(int rows)
{
    if (!transactionService.IsActive) await transactionService.BeginTransactionAsync();
    for (var i = 1; i <= rows; i++)
    {
        await ExecAsync(
            $"INSERT INTO IMP_TARGET (ID, CODE, NAME, QTY, PRICE) VALUES ({i}, 'SEED{i}', 'seed', 1, 1.00)");
    }
    await transactionService.CommitAsync();
}

static string Csv(int rows, int firstId)
{
    var sb = new System.Text.StringBuilder("ID;CODE;NAME;QTY;PRICE\n");
    for (var i = 0; i < rows; i++)
    {
        var id = firstId + i;
        sb.Append(id).Append(";C").Append(id).Append(";Widget ").Append(id)
          .Append(';').Append(i % 50).Append(';').Append((i % 90) + 1).Append(".50\n");
    }
    return sb.ToString();
}

var target = await targetReader.ReadTargetAsync("IMP_TARGET", CancellationToken.None)
    ?? throw new InvalidOperationException("IMP_TARGET not found in the lab database.");

// The whole surface's configuration, built exactly as DataImportTabViewModel.BuildConfiguration would.
ImportConfiguration ConfigurationFor(
    ImportTransactionMode mode, int commitEvery = 10_000, int batchSize = ImportConfiguration.DefaultBatchSize)
    => ImportConfiguration.Empty with
    {
        Delimited = new DelimitedOptions { Delimiter = ';', AutoDetectDelimiter = false, HasHeader = true },
        Culture = new ImportCultureOptions { DecimalSeparator = '.' },
        Target = TargetDescriptor.Existing("IMP_TARGET"),
        Mapping = new[]
        {
            new ColumnMapping { TargetColumnName = "ID", SourceFieldName = "ID", SourceFieldIndex = 0 },
            new ColumnMapping { TargetColumnName = "CODE", SourceFieldName = "CODE", SourceFieldIndex = 1 },
            new ColumnMapping { TargetColumnName = "NAME", SourceFieldName = "NAME", SourceFieldIndex = 2 },
            new ColumnMapping { TargetColumnName = "QTY", SourceFieldName = "QTY", SourceFieldIndex = 3 },
            new ColumnMapping { TargetColumnName = "PRICE", SourceFieldName = "PRICE", SourceFieldIndex = 4 },
        },
        Transaction = mode,
        CommitEveryRows = commitEvery,
        BatchSize = batchSize,
        ErrorPolicy = ImportErrorPolicy.SkipInvalidRows,
    };

var provider = new DelimitedTextImportProvider();
var charset = ImportCharsetGuard.Strict(profile.Charset);

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("A — CSV into an existing table: the report's numbers ARE the table's numbers");

await ResetAsync();
{
    var configuration = ConfigurationFor(ImportTransactionMode.Manual);
    var writer = new FirebirdImportWriter(transactionService, configuration.ErrorPolicy);

    var outcome = await ImportPipeline.RunAsync(
        configuration, target, provider, new TextImportSource(Csv(500, 1)), writer, charset);

    if (!outcome.TransactionLeftOpen)
        Fail("A1 transaction stays open", "the writer committed, which rule #3 forbids");
    else
        Pass("A1 transaction stays open", "auto-begin, never auto-commit (rule #3)");

    await transactionService.CommitAsync();

    var actual = await CountCommittedAsync();
    if (actual == outcome.RowsWritten && actual == 500)
        Pass("A2 report == SELECT COUNT(*)", $"{outcome.RowsWritten} written, {actual} in the table");
    else
        Fail("A2 report == SELECT COUNT(*)", $"report {outcome.RowsWritten}, table {actual}");

    if (outcome.RowsRead == 500 && outcome.RowsFailed == 0)
        Pass("A3 counters", $"read {outcome.RowsRead}, failed {outcome.RowsFailed}");
    else
        Fail("A3 counters", $"read {outcome.RowsRead}, failed {outcome.RowsFailed}");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("B — Rollback takes the whole import back, DELETE included (§4.5 / D5)");

await ResetAsync();
await SeedAsync(4);
{
    var before = await CountCommittedAsync();

    // The surface's order: empty the target, then write — both in the SAME working transaction.
    var emptied = await preparer.EmptyAsync("IMP_TARGET", CancellationToken.None);

    var configuration = ConfigurationFor(ImportTransactionMode.Manual);
    var writer = new FirebirdImportWriter(transactionService, configuration.ErrorPolicy);
    var outcome = await ImportPipeline.RunAsync(
        configuration, target, provider, new TextImportSource(Csv(10, 100)), writer, charset);

    await transactionService.RollbackAsync();
    var after = await CountCommittedAsync();

    if (emptied == before)
        Pass("B1 DELETE reports what it removed", $"{emptied} row(s)");
    else
        Fail("B1 DELETE reports what it removed", $"deleted {emptied}, table held {before}");

    if (after == before && outcome.RowsWritten == 10)
        Pass("B2 Rollback undoes DELETE + INSERTs", $"{outcome.RowsWritten} written, table back at {after}");
    else
        Fail("B2 Rollback undoes DELETE + INSERTs", $"expected {before}, got {after}");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("C — the count behind the 'empty the table first' confirmation");

await ResetAsync();
await SeedAsync(6);
{
    var counted = await preparer.CountRowsAsync("IMP_TARGET", CancellationToken.None);
    var witness = 6L;

    if (counted == witness)
        Pass("C1 preparer counts the target", $"{counted} row(s)");
    else
        Fail("C1 preparer counts the target", $"preparer {counted}, expected {witness}");

    // ⭐ It must see what the transaction that will DO the deleting sees — including rows that transaction
    // itself has just inserted and not yet committed. A Metadata-lane count could not.
    await ExecAsync("INSERT INTO IMP_TARGET (ID, CODE, NAME) VALUES (999, 'UNCOMMIT', 'x')");
    var withUncommitted = await preparer.CountRowsAsync("IMP_TARGET", CancellationToken.None);
    await transactionService.RollbackAsync();

    if (withUncommitted == witness + 1)
        Pass("C2 counts inside the user's transaction", $"{withUncommitted} incl. the uncommitted row");
    else
        Fail("C2 counts inside the user's transaction", $"saw {withUncommitted}, expected {witness + 1}");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("D — Batched: commits every N, and Rollback cannot take those back (§0.5)");

await ResetAsync();
{
    // Batch 200 / commit every 400 -> commits after the 2nd and 4th flush (400, 800), leaving a 200-row tail
    // open. Chosen so the two numbers are DIFFERENT: a commit interval that merely equalled the batch size
    // would pass even if the writer committed on every flush.
    var configuration = ConfigurationFor(ImportTransactionMode.Batched, commitEvery: 400, batchSize: 200);
    var inner = new FirebirdImportWriter(transactionService, configuration.ErrorPolicy);
    var batched = new BatchedCommitImportWriter(inner, transactionService, configuration.CommitEveryRows);

    var outcome = await ImportPipeline.RunAsync(
        configuration, target, provider, new TextImportSource(Csv(1000, 1)), batched, charset);

    // The tail is deliberately left open: whether to keep the last partial batch is the user's decision,
    // taken in front of the report's numbers. Rolling back must therefore lose exactly the tail.
    await transactionService.RollbackAsync();
    var after = await CountCommittedAsync();

    if (batched.RowsCommitted == 800)
        Pass("D1 commits every N", $"{batched.RowsCommitted} committed, every {configuration.CommitEveryRows} rows");
    else
        Fail("D1 commits every N", $"committed {batched.RowsCommitted}, expected 800");

    if (after == batched.RowsCommitted)
        Pass("D2 Rollback loses only the tail", $"{after} row(s) survived — §0.5 is telling the truth");
    else
        Fail("D2 Rollback loses only the tail", $"table holds {after}, committed {batched.RowsCommitted}");

    if (outcome.RowsWritten == 1000)
        Pass("D3 the run itself wrote everything", $"{outcome.RowsWritten} written");
    else
        Fail("D3 the run itself wrote everything", $"{outcome.RowsWritten} written");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("E — Validate writes nothing, through the very same pipeline");

await ResetAsync();
{
    var configuration = ConfigurationFor(ImportTransactionMode.Manual);
    var dryRun = new DryRunImportWriter();

    var outcome = await ImportPipeline.RunAsync(
        configuration, target, provider, new TextImportSource(Csv(50, 1)), dryRun, charset);

    var after = await CountCommittedAsync();

    if (after == 0 && outcome.RowsWritten == 50 && !outcome.TransactionLeftOpen)
        Pass("E1 dry run touches nothing", $"{outcome.RowsWritten} checked, {after} row(s) in the table");
    else
        Fail("E1 dry run touches nothing", $"table holds {after}, TransactionLeftOpen={outcome.TransactionLeftOpen}");
}

await ResetAsync();
await connectionService.DisconnectAsync();

Console.WriteLine();
Console.WriteLine(failed == 0
    ? $"ALL PASS — {passed} check(s)."
    : $"{failed} FAILED, {passed} passed.");

return failed == 0 ? 0 : 1;
