// Data Import — etap I7 live verification. See DataImportRunProbe.csproj for what this is and why.
//
//   dotnet run --project tools/probes/DataImportRunProbe
//
// Requires the local FB5 DefaultInstance on localhost:3050 and Lab/EmberTern_Lab.fdb.

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EmberTern.Core.Connections;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using EmberTern.Firebird;
using EmberTern.Office;

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
// ⭐ I7.5: the module's OWN attachment and transaction. `transactionService` stays the CONSOLE's — which makes
// it the perfect independent witness for what the import did or did not persist.
var importSession = await connectionService.CreateImportSessionAsync();
var preparer = new FirebirdImportTargetPreparer(importSession);

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
    var writer = new FirebirdImportWriter(importSession, configuration.ErrorPolicy);

    var outcome = await ImportPipeline.RunAsync(
        configuration, target, provider, new TextImportSource(Csv(500, 1)), writer, charset);

    if (!outcome.TransactionLeftOpen)
        Fail("A1 transaction stays open", "the writer committed, which rule #3 forbids");
    else
        Pass("A1 transaction stays open", "auto-begin, never auto-commit (rule #3)");

    await importSession.CommitAsync();

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
    var writer = new FirebirdImportWriter(importSession, configuration.ErrorPolicy);
    var outcome = await ImportPipeline.RunAsync(
        configuration, target, provider, new TextImportSource(Csv(10, 100)), writer, charset);

    await importSession.RollbackAsync();
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
    var inner = new FirebirdImportWriter(importSession, configuration.ErrorPolicy);
    var batched = new BatchedCommitImportWriter(inner, importSession, configuration.CommitEveryRows);

    var outcome = await ImportPipeline.RunAsync(
        configuration, target, provider, new TextImportSource(Csv(1000, 1)), batched, charset);

    // The tail is deliberately left open: whether to keep the last partial batch is the user's decision,
    // taken in front of the report's numbers. Rolling back must therefore lose exactly the tail.
    await importSession.RollbackAsync();
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

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("F — the decision itself: the import cannot settle the console's work (I7.5)");

await ResetAsync();
{
    // The console writes something and leaves it open — the situation that used to entangle the two.
    if (!transactionService.IsActive) await transactionService.BeginTransactionAsync();
    await ExecAsync("INSERT INTO IMP_TARGET (ID, CODE, NAME) VALUES (7001, 'CONSOLE', 'console work')");

    var configuration = ConfigurationFor(ImportTransactionMode.Manual);
    var writer = new FirebirdImportWriter(importSession, configuration.ErrorPolicy);
    var outcome = await ImportPipeline.RunAsync(
        configuration, target, provider, new TextImportSource(Csv(20, 8000)), writer, charset);

    if (outcome.RowsWritten == 20)
        Pass("F1 import runs beside an open console tx", "20 written, neither blocked the other");
    else
        Fail("F1 import runs beside an open console tx", $"{outcome.RowsWritten} written");

    // ⭐ THE decision: committing the import must persist the import and NOTHING else.
    await importSession.CommitAsync();
    await transactionService.RollbackAsync();
    var after = await CountCommittedAsync();

    if (after == 20)
        Pass("F2 import Commit settled ONLY the import", $"{after} rows — the console's row rolled back with it");
    else
        Fail("F2 import Commit settled ONLY the import", $"{after} rows, expected 20");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("G — etap I8: a table that does not exist yet");

// The Ddl lane: autonomous, auto-committed, WAIT-bounded — the SAME executor the object editors compile
// through (§4.6). Everything this section proves rests on the CREATE being committed before the first row,
// which is gotcha #213 and not a preference.
var ddlExecutor = new FirebirdDdlExecutor(connectionService, transactionService);

const string newTable = "IMP_NEW_PROBE";

async Task<long> CountInAsync(string table)
{
    if (transactionService.IsActive) throw new InvalidOperationException("Count with no transaction open.");

    await transactionService.BeginTransactionAsync();
    var connection = connectionService.RequireOpenConnection();
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
    cmd.Transaction = transactionService.ActiveTransaction;
    var scalar = await cmd.ExecuteScalarAsync();
    await transactionService.CommitAsync();
    return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
}

async Task<bool> TableExistsAsync(string table)
{
    if (transactionService.IsActive) await transactionService.CommitAsync();
    await transactionService.BeginTransactionAsync();
    var connection = connectionService.RequireOpenConnection();
    await using var cmd = connection.CreateCommand();
    cmd.CommandText =
        "SELECT COUNT(*) FROM RDB$RELATIONS WHERE TRIM(RDB$RELATION_NAME) = @n AND RDB$VIEW_BLR IS NULL";
    cmd.Transaction = transactionService.ActiveTransaction;
    var parameter = cmd.CreateParameter();
    parameter.ParameterName = "@n";
    parameter.Value = table;
    cmd.Parameters.Add(parameter);
    var scalar = await cmd.ExecuteScalarAsync();
    await transactionService.CommitAsync();
    return Convert.ToInt64(scalar, CultureInfo.InvariantCulture) > 0;
}

async Task DropIfExistsAsync(string table)
{
    if (await TableExistsAsync(table))
    {
        await ddlExecutor.ExecuteAsync(ImportNewTable.BuildDropSql(table), CancellationToken.None);
    }
}

// ⭐ R19 in a real file: a column of whole numbers with ONE piece of text in it. A sample would type QTY as
// INTEGER and the import would then fail on that row — AFTER the table had been created and committed, i.e.
// at the worst possible moment. The whole-source scan is what prevents it.
const string newTableCsv =
    "KOD;ILOSC;CENA;DATA\n" +
    "A1;5;1.50;03.04.2026\n" +
    "A2;12;22.75;04.04.2026\n" +
    "A3;7;3.05;05.04.2026\n" +
    "A4;nie wiem;9.99;06.04.2026\n";

var newTableCulture = new ImportCultureOptions { DecimalSeparator = '.', DateOrder = DateFieldOrder.Dmy };

await DropIfExistsAsync(newTable);
{
    var inferConfiguration = ImportConfiguration.Empty with
    {
        Delimited = new DelimitedOptions { Delimiter = ';', AutoDetectDelimiter = false, HasHeader = true },
        Culture = newTableCulture,
    };

    var schema = await provider.ReadSchemaAsync(
        new TextImportSource(newTableCsv), inferConfiguration, CancellationToken.None);

    var inference = await ColumnTypeInferencer.InferAsync(
        schema, provider, new TextImportSource(newTableCsv), inferConfiguration,
        ColumnTypeInferencer.DefaultScanLimit, CancellationToken.None);

    var inferred = inference.Columns.Select(c => c.Definition).ToList();
    var types = inferred.Select(ImportNewTable.TypeText).ToArray();

    // §0.3: the mixed column falls to VARCHAR, and the evidence names the value that decided it.
    var qty = inference.Columns[1];
    if (types[1].StartsWith("VARCHAR", StringComparison.Ordinal) && qty.Evidence.RejectedByValue == "nie wiem")
        Pass("G1 mixed column falls to VARCHAR", $"ILOSC -> {types[1]}, decided by row {qty.Evidence.RejectedAtRow}");
    else
        Fail("G1 mixed column falls to VARCHAR", $"ILOSC -> {types[1]}, rejected by {qty.Evidence.RejectedByValue}");

    if (types[2] == "NUMERIC(4,2)" && types[3] == "DATE" && inference.RowsAnalysed == 4)
        Pass("G2 the other columns type from the whole file", $"CENA {types[2]}, DATA {types[3]}, {inference.RowsAnalysed} rows");
    else
        Fail("G2 the other columns type from the whole file", $"CENA {types[2]}, DATA {types[3]}, {inference.RowsAnalysed} rows");

    // ── The CREATE, on the Ddl lane, committed before any row (#213) ─────────────────────────────────────
    var createSql = ImportNewTable.BuildCreateSql(newTable, inferred);
    await ddlExecutor.ExecuteAsync(createSql, CancellationToken.None);

    if (await TableExistsAsync(newTable))
        Pass("G3 CREATE committed on the Ddl lane", "visible from another attachment straight away (#213)");
    else
        Fail("G3 CREATE committed on the Ddl lane", "the table is not visible to the console attachment");

    // ⭐⭐ The invariant the unit tests can only assert against our own parser: does FIREBIRD report back the
    // type we asked for? If it does not, the preview validated rows against a column that does not exist as
    // described — which is exactly the drift ImportNewTable exists to make impossible.
    var created = await targetReader.ReadTargetAsync(newTable, CancellationToken.None);
    if (created is not null)
    {
        var catalogTypes = created.Columns.Select(c => c.Type).ToArray();
        if (catalogTypes.SequenceEqual(types))
            Pass("G4 catalog reports the types we asked for", string.Join(", ", catalogTypes));
        else
            Fail("G4 catalog reports the types we asked for", $"asked {string.Join(", ", types)}, got {string.Join(", ", catalogTypes)}");
    }
    else
    {
        Fail("G4 catalog reports the types we asked for", "the created table could not be read back");
    }

    // ── The rows ────────────────────────────────────────────────────────────────────────────────────────
    var runConfiguration = ImportConfiguration.Empty with
    {
        Delimited = new DelimitedOptions { Delimiter = ';', AutoDetectDelimiter = false, HasHeader = true },
        Culture = newTableCulture,
        Target = TargetDescriptor.New(newTable, inferred),
        Mapping = inferred.Select((c, i) => new ColumnMapping
        {
            TargetColumnName = c.Name,
            SourceFieldName = schema.Fields[i].Name,
            SourceFieldIndex = i,
        }).ToArray(),
        ErrorPolicy = ImportErrorPolicy.SkipInvalidRows,
    };

    var writer = new FirebirdImportWriter(importSession, runConfiguration.ErrorPolicy);
    var outcome = await ImportPipeline.RunAsync(
        runConfiguration, created!, provider, new TextImportSource(newTableCsv), writer, charset);

    await importSession.CommitAsync();

    var actual = await CountInAsync(newTable);
    if (outcome.RowsWritten == 4 && actual == 4 && outcome.RowsFailed == 0)
        Pass("G5 report == SELECT COUNT(*)", $"{outcome.RowsWritten} written, {actual} in the new table");
    else
        Fail("G5 report == SELECT COUNT(*)", $"report {outcome.RowsWritten}/{outcome.RowsFailed} failed, table {actual}");

    // ⭐⭐ THE sentence the surface shows the user, verified against the engine: a Rollback takes the ROWS
    // back and leaves the TABLE. If this ever failed, the warning in the Target section would be a lie —
    // in one direction or the other.
    var second = new FirebirdImportWriter(importSession, runConfiguration.ErrorPolicy);
    await ImportPipeline.RunAsync(
        runConfiguration, created!, provider, new TextImportSource(newTableCsv), second, charset);

    await importSession.RollbackAsync();

    var afterRollback = await CountInAsync(newTable);
    var survived = await TableExistsAsync(newTable);

    if (survived && afterRollback == 4)
        Pass("G6 Rollback undoes the rows, NOT the table", $"table still there, {afterRollback} rows (§0.5)");
    else
        Fail("G6 Rollback undoes the rows, NOT the table", $"exists={survived}, rows={afterRollback}, expected true/4");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("H — etap I9: the SAME journey, from a workbook");

// ⭐ The point of this section is how little of it is new. Only the provider changes; the target reader, the
// pipeline, the writer, the transaction and the report are the ones sections A–G already proved. If reading a
// sheet needed anything else, the "one pipeline for every source" pillar (§1.4) would not be true.
var xlsxProvider = new XlsxImportProvider();
const string xlsxNewTable = "IMP_NEW_XLSX_PROBE";
var workbookPath = Path.Combine(Path.GetTempPath(), $"embertern-i9-probe-{Guid.NewGuid():N}.xlsx");
var errorBookPath = Path.Combine(Path.GetTempPath(), $"embertern-i9-error-{Guid.NewGuid():N}.xlsx");
var sheetDate = new DateTime(2026, 4, 3);

// Two workbooks on purpose: one clean, one carrying a single #N/A. Putting the error cell in the same sheet
// would also feed it to the type inferencer, and then H2 would be testing two things at once.
BuildProbeWorkbook(workbookPath, sheetDate);
BuildErrorWorkbook(errorBookPath);

try
{
    // ── H1: a workbook into the EXISTING table ──────────────────────────────────────────────────────────
    await ResetAsync();
    {
        var configuration = ConfigurationFor(ImportTransactionMode.Manual) with
        {
            Source = SourceDescriptor.File(ImportSourceKind.Xlsx, workbookPath),
            Delimited = null,
            Spreadsheet = new SpreadsheetOptions { HasHeader = true, FirstDataRow = 2 },
        };

        var writer = new FirebirdImportWriter(importSession, configuration.ErrorPolicy);
        var outcome = await ImportPipeline.RunAsync(
            configuration, target, xlsxProvider, new FileImportSource(workbookPath), writer, charset);

        await importSession.CommitAsync();

        var actual = await CountCommittedAsync();
        if (outcome.RowsWritten == 3 && actual == 3 && outcome.RowsFailed == 0)
            Pass("H1 workbook -> existing table", $"{outcome.RowsWritten} written, {actual} in the table");
        else
            Fail("H1 workbook -> existing table", $"report {outcome.RowsWritten}/{outcome.RowsFailed} failed, table {actual}");
    }

    // ── H1b: R20 on a live engine, against a VARCHAR column ─────────────────────────────────────────────
    await ResetAsync();
    {
        var configuration = ConfigurationFor(ImportTransactionMode.Manual) with
        {
            Source = SourceDescriptor.File(ImportSourceKind.Xlsx, errorBookPath),
            Delimited = null,
            Spreadsheet = new SpreadsheetOptions { HasHeader = true, FirstDataRow = 2 },
        };

        var writer = new FirebirdImportWriter(importSession, configuration.ErrorPolicy);
        var outcome = await ImportPipeline.RunAsync(
            configuration, target, xlsxProvider, new FileImportSource(errorBookPath), writer, charset);

        await importSession.CommitAsync();

        // ⭐ The sheet's last row has #N/A in NAME, a VARCHAR column — the exact case R20 names. A text column
        // accepts anything, so if the refusal were left to the target type this row would land with "#N/A"
        // sitting in it as though it were data.
        var actual = await CountCommittedAsync();
        if (actual == 3 && outcome.RowsFailed == 1
            && outcome.Errors.Any(e => e.Kind == ImportErrorKind.SourceErrorValue))
            Pass("H1b #N/A refused by a VARCHAR column", $"{actual} rows in, 1 refused — R20 holds on the engine");
        else
            Fail("H1b #N/A refused by a VARCHAR column",
                $"rows {actual}, failed {outcome.RowsFailed}, kinds {string.Join("/", outcome.Errors.Select(e => e.Kind))}");
    }

    // ── H2/H3/H4: a workbook into a table that does not exist yet (I8 + I9 together) ─────────────────────
    await DropIfExistsAsync(xlsxNewTable);
    {
        var inferConfiguration = ImportConfiguration.Empty with
        {
            Source = SourceDescriptor.File(ImportSourceKind.Xlsx, workbookPath),
            Delimited = null,
            Spreadsheet = new SpreadsheetOptions { HasHeader = true, FirstDataRow = 2 },
            Culture = newTableCulture,
        };

        var sheetSource = new FileImportSource(workbookPath);
        var schema = await xlsxProvider.ReadSchemaAsync(sheetSource, inferConfiguration, CancellationToken.None);

        var inference = await ColumnTypeInferencer.InferAsync(
            schema, xlsxProvider, sheetSource, inferConfiguration,
            ColumnTypeInferencer.DefaultScanLimit, CancellationToken.None);

        var inferred = inference.Columns.Select(c => c.Definition).ToList();
        var types = inferred.Select(ImportNewTable.TypeText).ToArray();

        // ⭐⭐ THE I9 claim: inference works on a sheet with NO change, because the provider hands over native
        // values and ColumnTypeInferencer asks ImportValueConverter — the same class either way. A real Excel
        // DATE cell must therefore become a DATE column without anyone parsing a date string.
        if (types.Length == 6 && types[5] == "DATE")
            Pass("H2 a real date CELL types as DATE", string.Join(", ", types));
        else
            Fail("H2 a real date CELL types as DATE", string.Join(", ", types));

        await ddlExecutor.ExecuteAsync(ImportNewTable.BuildCreateSql(xlsxNewTable, inferred), CancellationToken.None);
        var created = await targetReader.ReadTargetAsync(xlsxNewTable, CancellationToken.None);

        var runConfiguration = inferConfiguration with
        {
            Target = TargetDescriptor.New(xlsxNewTable, inferred),
            Mapping = inferred.Select((c, i) => new ColumnMapping
            {
                TargetColumnName = c.Name,
                SourceFieldName = schema.Fields[i].Name,
                SourceFieldIndex = i,
            }).ToArray(),
            ErrorPolicy = ImportErrorPolicy.SkipInvalidRows,
        };

        var writer = new FirebirdImportWriter(importSession, runConfiguration.ErrorPolicy);
        var outcome = await ImportPipeline.RunAsync(
            runConfiguration, created!, xlsxProvider, sheetSource, writer, charset);

        await importSession.CommitAsync();

        var landed = await CountInAsync(xlsxNewTable);
        if (landed == 3 && outcome.RowsWritten == 3 && outcome.RowsFailed == 0)
            Pass("H3 workbook -> a table that did not exist", $"{outcome.RowsWritten} written, {landed} in {xlsxNewTable}");
        else
            Fail("H3 workbook -> a table that did not exist",
                $"report {outcome.RowsWritten}/{outcome.RowsFailed} failed, table {landed}");

        // ⭐ The date round trip, end to end: a serial number in a workbook, a DATE column in Firebird, and the
        // same calendar day coming back out. Anything else here would be §0.1 with extra steps.
        if (transactionService.IsActive) await transactionService.CommitAsync();
        await transactionService.BeginTransactionAsync();
        var connection = connectionService.RequireOpenConnection();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"SELECT MIN(DATA) FROM {xlsxNewTable}";
            cmd.Transaction = transactionService.ActiveTransaction;
            var scalar = await cmd.ExecuteScalarAsync();
            var readBack = scalar is DateTime dt ? dt : default;

            if (readBack == sheetDate)
                Pass("H4 the date survives the round trip", $"sheet {sheetDate:yyyy-MM-dd} == database {readBack:yyyy-MM-dd}");
            else
                Fail("H4 the date survives the round trip", $"sheet {sheetDate:yyyy-MM-dd}, database {readBack:yyyy-MM-dd}");
        }
        await transactionService.CommitAsync();
    }
}
finally
{
    await DropIfExistsAsync(xlsxNewTable);
    try { File.Delete(workbookPath); } catch (IOException) { }
    try { File.Delete(errorBookPath); } catch (IOException) { }
}

// The clean workbook: the five columns IMP_TARGET wants, plus a sixth holding a REAL date cell — a serial
// number carrying a date number-format, which I0 measured is the only signal a date exists at all.
static void BuildProbeWorkbook(string path, DateTime date)
{
    using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
    var workbookPart = document.AddWorkbookPart();
    workbookPart.Workbook = new Workbook();

    var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
    stylesPart.Stylesheet = new Stylesheet(
        new Fonts(new Font()),
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None })),
        new Borders(new Border()),
        new CellStyleFormats(new CellFormat()),
        new CellFormats(
            new CellFormat(),
            new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true })); // built-in date format

    static Cell Text(string reference, string value) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value)),
    };
    static Cell Num(string reference, string value, uint style = 0) => new()
    {
        CellReference = reference,
        CellValue = new CellValue(value),
        StyleIndex = style,
    };
    static Row Line(uint index, params Cell[] cells)
    {
        var row = new Row { RowIndex = index };
        foreach (var cell in cells) row.Append(cell);
        return row;
    }

    var serial = date.ToOADate().ToString(CultureInfo.InvariantCulture);
    var sheetData = new SheetData();

    sheetData.Append(Line(1,
        Text("A1", "ID"), Text("B1", "CODE"), Text("C1", "NAME"),
        Text("D1", "QTY"), Text("E1", "PRICE"), Text("F1", "DATA")));
    sheetData.Append(Line(2,
        Num("A2", "1"), Text("B2", "X1"), Text("C2", "Widget 1"),
        Num("D2", "5"), Num("E2", "1.50"), Num("F2", serial, 1)));
    sheetData.Append(Line(3,
        Num("A3", "2"), Text("B3", "X2"), Text("C3", "Widget 2"),
        Num("D3", "12"), Num("E3", "22.75"), Num("F3", serial, 1)));
    sheetData.Append(Line(4,
        Num("A4", "3"), Text("B4", "X3"), Text("C4", "Widget 3"),
        Num("D4", "7"), Num("E4", "3.05"), Num("F4", serial, 1)));

    Finish(workbookPart, sheetData);
}

// The workbook whose last row carries a single #N/A, in a column whose target is VARCHAR.
static void BuildErrorWorkbook(string path)
{
    using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
    var workbookPart = document.AddWorkbookPart();
    workbookPart.Workbook = new Workbook();

    static Cell Text(string reference, string value) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value)),
    };
    static Cell Num(string reference, string value) => new()
    {
        CellReference = reference,
        CellValue = new CellValue(value),
    };
    static Row Line(uint index, params Cell[] cells)
    {
        var row = new Row { RowIndex = index };
        foreach (var cell in cells) row.Append(cell);
        return row;
    }

    var sheetData = new SheetData();
    sheetData.Append(Line(1,
        Text("A1", "ID"), Text("B1", "CODE"), Text("C1", "NAME"), Text("D1", "QTY"), Text("E1", "PRICE")));
    sheetData.Append(Line(2,
        Num("A2", "1"), Text("B2", "X1"), Text("C2", "Widget 1"), Num("D2", "5"), Num("E2", "1.50")));
    sheetData.Append(Line(3,
        Num("A3", "2"), Text("B3", "X2"), Text("C3", "Widget 2"), Num("D3", "12"), Num("E3", "22.75")));
    sheetData.Append(Line(4,
        Num("A4", "3"), Text("B4", "X3"), Text("C4", "Widget 3"), Num("D4", "7"), Num("E4", "3.05")));
    sheetData.Append(Line(5,
        Num("A5", "4"),
        Text("B5", "X4"),
        new Cell { CellReference = "C5", DataType = CellValues.Error, CellValue = new CellValue("#N/A") },
        Num("D5", "9"), Num("E5", "4.20")));

    Finish(workbookPart, sheetData);
}

static void Finish(WorkbookPart workbookPart, SheetData sheetData)
{
    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    worksheetPart.Worksheet = new Worksheet(sheetData);
    workbookPart.Workbook.AppendChild(new Sheets(new Sheet
    {
        Id = workbookPart.GetIdOfPart(worksheetPart),
        SheetId = 1U,
        Name = "Arkusz1",
    }));
    workbookPart.Workbook.Save();
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Section("I — etap I10: the clipboard, and the legacy .xls");

// ⭐ Note what is NOT here: a case proving the clipboard reaches the database. Every section from A onward has
// been importing out of a TextImportSource, and that IS the clipboard's path — App reads the clipboard and
// hands Core a string (§1.5). The one thing worth checking separately is the shape a paste from Excel actually
// has, which is TAB-separated and relies on detection rather than on the CSV default.
const string xlsNewTable = "IMP_NEW_XLS_PROBE";
var legacyPath = Path.Combine(Path.GetTempPath(), $"embertern-i10-probe-{Guid.NewGuid():N}.xls");
var xlsDate = new DateTime(2026, 5, 14);

BuildLegacyProbeWorkbook(legacyPath, xlsDate);
var xlsProvider = new XlsImportProvider();

try
{
    // ── I1: a paste out of Excel — TAB-separated text, with the separator DETECTED ───────────────────────
    await ResetAsync();
    {
        var pasted = "ID\tCODE\tNAME\tQTY\tPRICE\r\n1\tX1\tWidget 1\t5\t1.50\r\n2\tX2\tWidget 2\t12\t22.75\r\n";

        // ⭐ Detection is deliberately a step ABOVE the pipeline, and this case has to mirror that or it proves
        // nothing about the real surface. §0.4 lets auto-detection PROPOSE and never decide silently, so the
        // App runs the detector, shows the evidence, and writes the RESOLVED separator into the configuration —
        // which is why the provider reads a declared delimiter and has no opinion about detection at all. A
        // first version of this case passed AutoDetectDelimiter to the pipeline and got one single column back;
        // that was the case being wrong about the architecture, not the architecture being wrong.
        var declared = new DelimitedOptions { HasHeader = true, FirstDataRow = 2, AutoDetectDelimiter = true };
        var proposal = DelimiterDetector.Propose(pasted, declared);

        if (proposal is { Delimiter: '\t', IsUnanimous: true })
            Pass("I1a the detector proposes TAB for an Excel paste",
                $"{proposal.ConsistentRecords}/{proposal.SampledRecords} records agree on {proposal.FieldCount} fields");
        else
            Fail("I1a the detector proposes TAB for an Excel paste", proposal is null ? "no proposal" : $"'{proposal.Delimiter}'");

        var configuration = ConfigurationFor(ImportTransactionMode.Manual) with
        {
            Source = SourceDescriptor.Clipboard(),
            Delimited = declared with { Delimiter = proposal?.Delimiter ?? ';' },
        };

        var source = new TextImportSource(pasted);
        var schema = await provider.ReadSchemaAsync(source, configuration, CancellationToken.None);

        var writer = new FirebirdImportWriter(importSession, configuration.ErrorPolicy);
        var outcome = await ImportPipeline.RunAsync(configuration, target, provider, source, writer, charset);

        await importSession.CommitAsync();

        var actual = await CountCommittedAsync();
        if (schema.Fields.Count == 5 && outcome.RowsWritten == 2 && actual == 2 && outcome.RowsFailed == 0)
            Pass("I1 an Excel paste imports with no file at all", $"TAB detected, 5 fields, {actual} rows in the table");
        else
            Fail("I1 an Excel paste imports with no file at all",
                $"{schema.Fields.Count} fields, report {outcome.RowsWritten}/{outcome.RowsFailed} failed, table {actual}");
    }

    // ── I2: a legacy workbook into the EXISTING table ────────────────────────────────────────────────────
    await ResetAsync();
    {
        var configuration = ConfigurationFor(ImportTransactionMode.Manual) with
        {
            Source = SourceDescriptor.File(ImportSourceKind.Xls, legacyPath),
            Delimited = null,
            // Windowed to the three clean rows: the fixture's row 5 carries the #N/A cell, and that is I2b's
            // question. One case, one thing.
            Spreadsheet = new SpreadsheetOptions { HasHeader = true, FirstDataRow = 2, LastRow = 4 },
        };

        var writer = new FirebirdImportWriter(importSession, configuration.ErrorPolicy);
        var outcome = await ImportPipeline.RunAsync(
            configuration, target, xlsProvider, new FileImportSource(legacyPath), writer, charset);

        await importSession.CommitAsync();

        var actual = await CountCommittedAsync();
        if (outcome.RowsWritten == 3 && actual == 3 && outcome.RowsFailed == 0)
            Pass("I2 legacy .xls -> existing table", $"{outcome.RowsWritten} written, {actual} in the table");
        else
            Fail("I2 legacy .xls -> existing table",
                $"report {outcome.RowsWritten}/{outcome.RowsFailed} failed, table {actual}");
    }

    // ── I2b: R20 again, on the other container ───────────────────────────────────────────────────────────
    await ResetAsync();
    {
        var configuration = ConfigurationFor(ImportTransactionMode.Manual) with
        {
            Source = SourceDescriptor.File(ImportSourceKind.Xls, legacyPath),
            Delimited = null,
            // Row 5 of the fixture carries #N/A in NAME, a VARCHAR column. Widening the window to include it.
            Spreadsheet = new SpreadsheetOptions { HasHeader = true, FirstDataRow = 2, LastRow = 5 },
        };

        var writer = new FirebirdImportWriter(importSession, configuration.ErrorPolicy);
        var outcome = await ImportPipeline.RunAsync(
            configuration, target, xlsProvider, new FileImportSource(legacyPath), writer, charset);

        await importSession.CommitAsync();

        // ⭐ The marker a .xls raises is the SAME source-neutral SourceErrorValue a .xlsx raises, which is why
        // the converter needed no branch for this format — R20 closed once, for every source.
        var actual = await CountCommittedAsync();
        if (actual == 3 && outcome.RowsFailed == 1
            && outcome.Errors.Any(e => e.Kind == ImportErrorKind.SourceErrorValue))
            Pass("I2b #N/A refused by a VARCHAR column, from .xls", $"{actual} rows in, 1 refused");
        else
            Fail("I2b #N/A refused by a VARCHAR column, from .xls",
                $"rows {actual}, failed {outcome.RowsFailed}, kinds {string.Join("/", outcome.Errors.Select(e => e.Kind))}");
    }

    // ── I3/I4: a legacy workbook into a table that does not exist yet ────────────────────────────────────
    await DropIfExistsAsync(xlsNewTable);
    {
        var inferConfiguration = ImportConfiguration.Empty with
        {
            Source = SourceDescriptor.File(ImportSourceKind.Xls, legacyPath),
            Delimited = null,
            Spreadsheet = new SpreadsheetOptions { HasHeader = true, FirstDataRow = 2, LastRow = 4 },
            Culture = newTableCulture,
        };

        var sheetSource = new FileImportSource(legacyPath);
        var schema = await xlsProvider.ReadSchemaAsync(sheetSource, inferConfiguration, CancellationToken.None);

        var inference = await ColumnTypeInferencer.InferAsync(
            schema, xlsProvider, sheetSource, inferConfiguration,
            ColumnTypeInferencer.DefaultScanLimit, CancellationToken.None);

        var inferred = inference.Columns.Select(c => c.Definition).ToList();
        var types = inferred.Select(ImportNewTable.TypeText).ToArray();

        // ⭐⭐ The I10 claim, and it is the same sentence as I9's: type inference works on a THIRD source with
        // no change, because the provider hands over native values and ColumnTypeInferencer asks
        // ImportValueConverter. A BIFF date cell becomes a DATE column with nobody parsing a date string.
        if (types.Length == 6 && types[5] == "DATE")
            Pass("I3 a BIFF date CELL types as DATE", string.Join(", ", types));
        else
            Fail("I3 a BIFF date CELL types as DATE", string.Join(", ", types));

        await ddlExecutor.ExecuteAsync(ImportNewTable.BuildCreateSql(xlsNewTable, inferred), CancellationToken.None);
        var created = await targetReader.ReadTargetAsync(xlsNewTable, CancellationToken.None);

        var runConfiguration = inferConfiguration with
        {
            Target = TargetDescriptor.New(xlsNewTable, inferred),
            Mapping = inferred.Select((c, i) => new ColumnMapping
            {
                TargetColumnName = c.Name,
                SourceFieldName = schema.Fields[i].Name,
                SourceFieldIndex = i,
            }).ToArray(),
            ErrorPolicy = ImportErrorPolicy.SkipInvalidRows,
        };

        var writer = new FirebirdImportWriter(importSession, runConfiguration.ErrorPolicy);
        var outcome = await ImportPipeline.RunAsync(
            runConfiguration, created!, xlsProvider, sheetSource, writer, charset);

        await importSession.CommitAsync();

        var landed = await CountInAsync(xlsNewTable);
        if (landed == 3 && outcome.RowsWritten == 3 && outcome.RowsFailed == 0)
            Pass("I3b legacy .xls -> a table that did not exist", $"{outcome.RowsWritten} written, {landed} in {xlsNewTable}");
        else
            Fail("I3b legacy .xls -> a table that did not exist",
                $"report {outcome.RowsWritten}/{outcome.RowsFailed} failed, table {landed}");

        // ⭐ The date round trip through the OTHER container. Worth its own case because the two providers reach
        // a date by opposite routes — .xlsx decodes the serial itself, .xls receives a DateTime the library has
        // already decoded — and both have to land on the same calendar day (§0.1).
        if (transactionService.IsActive) await transactionService.CommitAsync();
        await transactionService.BeginTransactionAsync();
        var connection = connectionService.RequireOpenConnection();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"SELECT MIN(DATA) FROM {xlsNewTable}";
            cmd.Transaction = transactionService.ActiveTransaction;
            var scalar = await cmd.ExecuteScalarAsync();
            var readBack = scalar is DateTime dt ? dt : default;

            if (readBack == xlsDate)
                Pass("I4 the date survives the round trip from .xls", $"sheet {xlsDate:yyyy-MM-dd} == database {readBack:yyyy-MM-dd}");
            else
                Fail("I4 the date survives the round trip from .xls", $"sheet {xlsDate:yyyy-MM-dd}, database {readBack:yyyy-MM-dd}");
        }
        await transactionService.CommitAsync();
    }

    // ── I5: the user's OWN .xls, if it is on this machine ────────────────────────────────────────────────
    //
    // ⚠ I9's lesson, applied: a probe proves what it happened to execute, and everything above ran on a file
    // this probe wrote itself. NPOI's output is not Excel's. So when a real legacy workbook is present it is
    // read through the production provider as well — no import, just "can we actually read what Excel wrote".
    {
        var real = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Nadgodziny2.xls");

        if (!File.Exists(real))
        {
            Console.WriteLine("  I5 skipped — no real .xls on this machine to read.");
        }
        else
        {
            var realSource = new FileImportSource(real);
            var sheets = await xlsProvider.ListSheetsAsync(realSource, CancellationToken.None);
            var realConfiguration = ImportConfiguration.Empty with
            {
                Source = SourceDescriptor.File(ImportSourceKind.Xls, real),
                Delimited = null,
                Spreadsheet = new SpreadsheetOptions { HasHeader = true, FirstDataRow = 2, SheetIndex = 1 },
            };

            var realSchema = await xlsProvider.ReadSchemaAsync(realSource, realConfiguration, CancellationToken.None);

            var rows = 0;
            var lastRowNumber = 0;
            await foreach (var record in xlsProvider.ReadRecordsAsync(
                realSource, realConfiguration, CancellationToken.None))
            {
                rows++;
                lastRowNumber = record.SourceRowNumber;
            }

            // Row numbers must stay ordered and reach at least as far as the row count — the §0.6 property, on
            // a file nobody here wrote.
            if (sheets.Count > 0 && realSchema.Fields.Count > 0 && rows > 0 && lastRowNumber >= rows)
                Pass("I5 a REAL Excel-written .xls reads",
                    $"{sheets.Count} sheet(s), {realSchema.Fields.Count} fields, {rows} rows, last row #{lastRowNumber}");
            else
                Fail("I5 a REAL Excel-written .xls reads",
                    $"sheets {sheets.Count}, fields {realSchema.Fields.Count}, rows {rows}, last #{lastRowNumber}");
        }
    }
}
finally
{
    await DropIfExistsAsync(xlsNewTable);
    try { File.Delete(legacyPath); } catch (IOException) { }
}

// The legacy counterpart of BuildProbeWorkbook: the same six columns, so sections H and I ask the engine
// literally the same question through two different containers. Row 5 carries the #N/A case (I2b).
static void BuildLegacyProbeWorkbook(string path, DateTime date)
{
    var workbook = new NPOI.HSSF.UserModel.HSSFWorkbook();
    var sheet = workbook.CreateSheet("Arkusz1");

    var dateStyle = workbook.CreateCellStyle();
    dateStyle.DataFormat = workbook.CreateDataFormat().GetFormat("yyyy-mm-dd");

    var header = sheet.CreateRow(0);
    foreach (var (name, i) in new[] { "ID", "CODE", "NAME", "QTY", "PRICE", "DATA" }.Select((n, i) => (n, i)))
    {
        header.CreateCell(i).SetCellValue(name);
    }

    void Line(int index, double id, string code, string? name, double qty, double price)
    {
        var row = sheet.CreateRow(index);
        row.CreateCell(0).SetCellValue(id);
        row.CreateCell(1).SetCellValue(code);

        var nameCell = row.CreateCell(2);
        if (name is null) nameCell.SetCellErrorValue(42); // #N/A
        else nameCell.SetCellValue(name);

        row.CreateCell(3).SetCellValue(qty);
        row.CreateCell(4).SetCellValue(price);

        var dateCell = row.CreateCell(5);
        dateCell.SetCellValue(date);
        dateCell.CellStyle = dateStyle;
    }

    Line(1, 1, "X1", "Widget 1", 5, 1.50);
    Line(2, 2, "X2", "Widget 2", 12, 22.75);
    Line(3, 3, "X3", "Widget 3", 7, 3.05);
    Line(4, 4, "X4", null, 9, 4.20);

    using var output = File.Create(path);
    workbook.Write(output, leaveOpen: true);
}

// ── The offered clean-up: roll back, THEN drop (§0.5) ────────────────────────────────────────────────────
{
    await ddlExecutor.ExecuteAsync(ImportNewTable.BuildDropSql(newTable), CancellationToken.None);

    if (!await TableExistsAsync(newTable))
        Pass("G7 the clean-up really removes the table", "DROP on the Ddl lane, after the rows are gone");
    else
        Fail("G7 the clean-up really removes the table", "the table is still there");
}

await DropIfExistsAsync(newTable);
await ResetAsync();
await importSession.DisposeAsync();
await connectionService.DisconnectAsync();

Console.WriteLine();
Console.WriteLine(failed == 0
    ? $"ALL PASS — {passed} check(s)."
    : $"{failed} FAILED, {passed} passed.");

return failed == 0 ? 0 : 1;
