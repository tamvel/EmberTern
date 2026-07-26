using System.Diagnostics;
using System.Globalization;
using System.Text;
using FirebirdSql.Data.FirebirdClient;

// DEVELOPER VERIFICATION TOOL — NOT PART OF THE PRODUCT. See tools/probes/README.md.
//
// Data Import — etap I0. See the .csproj header for what each phase (W / B / T / C) answers and why.
//
//   $env:ET_LAB_PWD = "<local dev SYSDBA password>"
//   dotnet run --project tools\probes\DataImportWriteProbe

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

var pwd = Environment.GetEnvironmentVariable("ET_LAB_PWD");
if (string.IsNullOrWhiteSpace(pwd))
{
    Console.WriteLine("Set ET_LAB_PWD to the local dev SYSDBA password.");
    return 2;
}

const string ScratchPath = @"C:\Temp\et_import_probe.fdb";
const int PerfRows = 50_000;
const int NaiveRows = 10_000;
const int ErrorRows = 5_000;
const int BadRowIndex = 2_500;   // 0-based position of the deliberately invalid row

int failures = 0;
void Fail(string what, string detail) { failures++; Console.WriteLine($"  FAIL  {what} — {detail}"); }
void Pass(string what, string detail) => Console.WriteLine($"  PASS  {what} — {detail}");
void Info(string what, string detail) => Console.WriteLine($"  ....  {what} — {detail}");
void Section(string title) => Console.WriteLine($"\n=== {title} " + new string('=', Math.Max(0, 74 - title.Length)));

string Cs(string charset) => new FbConnectionStringBuilder
{
    DataSource = "localhost",
    Port = 3050,
    Database = ScratchPath,
    UserID = "SYSDBA",
    Password = pwd,
    Charset = charset,
    Dialect = 3,
    ServerType = FbServerType.Default,
    Pooling = false,
}.ToString();

// Mirrors what the import writer will use: write · read committed rec_version · NOWAIT (gotcha #85 —
// never begin from a bare IsolationLevel).
static FbTransaction Begin(FbConnection c) => c.BeginTransaction(new FbTransactionOptions
{
    TransactionBehavior = FbTransactionBehavior.Write
                        | FbTransactionBehavior.ReadCommitted
                        | FbTransactionBehavior.RecVersion
                        | FbTransactionBehavior.NoWait,
});

static void Exec(FbConnection c, FbTransaction? tx, string sql)
{
    using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    if (tx is not null) cmd.Transaction = tx;
    cmd.ExecuteNonQuery();
}

static object? Scalar(FbConnection c, FbTransaction? tx, string sql)
{
    using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    if (tx is not null) cmd.Transaction = tx;
    return cmd.ExecuteScalar();
}

static long CountRows(FbConnection c, FbTransaction? tx, string table)
    => Convert.ToInt64(Scalar(c, tx, $"SELECT COUNT(*) FROM {table}") ?? 0L, CultureInfo.InvariantCulture);

// Deterministic row generator — same data for every phase, so timings are comparable.
static (int Id, string Code, string Name, int Qty, decimal Price, DateTime D) Row(int i) => (
    i,
    "EOP-375-GTO-EU-" + i.ToString("D6", CultureInfo.InvariantCulture),
    "(STD) MERCURY 2x400 XXL VERADO WHITE #" + i.ToString(CultureInfo.InvariantCulture),
    i % 97,
    decimal.Round(1000m + (i % 5000) * 1.37m, 2),
    new DateTime(2020, 1, 1).AddDays(i % 2000));

static void AddRowParameters(FbParameterCollection ps, int i)
{
    var r = Row(i);
    ps.Add(new FbParameter("@id", FbDbType.Integer) { Value = r.Id });
    ps.Add(new FbParameter("@code", FbDbType.VarChar, 30) { Value = r.Code });
    ps.Add(new FbParameter("@name", FbDbType.VarChar, 100) { Value = r.Name });
    ps.Add(new FbParameter("@qty", FbDbType.Integer) { Value = r.Qty });
    ps.Add(new FbParameter("@price", FbDbType.Numeric) { Value = r.Price });
    ps.Add(new FbParameter("@d", FbDbType.Date) { Value = r.D });
}

const string InsertSql =
    "INSERT INTO {0} (ID, CODE, NAME, QTY, PRICE, D) VALUES (@id, @code, @name, @qty, @price, @d)";

Console.WriteLine("Data Import — I0 write-path probe (FB5, scratch DB, raw driver)");
Console.WriteLine($"Scratch database: {ScratchPath}");

// ── Phase 0 — scratch database ───────────────────────────────────────────────────────────────────────
Section("Phase 0 — scratch database + default charset");
try
{
    FbConnection.CreateDatabase(Cs("WIN1250"), pageSize: 8192, forcedWrites: false, overwrite: true);
}
catch (Exception ex)
{
    Console.WriteLine($"  FAIL  create scratch DB — {ex.Message}");
    return 1;
}

try
{
    await using (var c = new FbConnection(Cs("WIN1250")))
    {
        await c.OpenAsync();
        var dbCharset = (Scalar(c, null, "SELECT RDB$CHARACTER_SET_NAME FROM RDB$DATABASE") as string)?.Trim();
        if (string.Equals(dbCharset, "WIN1250", StringComparison.OrdinalIgnoreCase))
            Pass("DB default charset", "WIN1250 — CreateDatabase honours the connection-string Charset");
        else
            Info("DB default charset", $"'{dbCharset}' — NOT taken from the connection string; DDL must say DEFAULT CHARACTER SET explicitly");

        Info("server version", c.ServerVersion);

        Exec(c, null, """
            CREATE TABLE IMP_PLAIN (
              ID INTEGER NOT NULL, CODE VARCHAR(30), NAME VARCHAR(100),
              QTY INTEGER, PRICE NUMERIC(15,2), D DATE)
            """);
        Exec(c, null, """
            CREATE TABLE IMP_KEYED (
              ID INTEGER NOT NULL PRIMARY KEY, CODE VARCHAR(30), NAME VARCHAR(100),
              QTY INTEGER, PRICE NUMERIC(15,2), D DATE)
            """);
        Exec(c, null, "CREATE INDEX IMP_KEYED_CODE ON IMP_KEYED (CODE)");
        Exec(c, null, """
            CREATE TABLE IMP_ERR (
              ID INTEGER NOT NULL, CODE VARCHAR(30), NAME VARCHAR(100),
              QTY INTEGER, PRICE NUMERIC(15,2), D DATE)
            """);
        Exec(c, null, "CREATE TABLE IMP_TRUNC (C VARCHAR(20))");
        Exec(c, null, """
            CREATE TABLE IMP_CS (
              TAG VARCHAR(20),
              C_1250 VARCHAR(50) CHARACTER SET WIN1250,
              C_UTF8 VARCHAR(50) CHARACTER SET UTF8)
            """);
        Pass("scratch schema", "IMP_PLAIN / IMP_KEYED (+PK,+index) / IMP_ERR / IMP_TRUNC / IMP_CS created");
    }

    // ── Phase W — throughput ─────────────────────────────────────────────────────────────────────────
    Section("Phase W — throughput (rows/s)");

    // W1 / W1b — prepared command, parameters re-bound per row, ONE transaction.
    async Task<double> PreparedLoop(string table, int rows, int commitEvery)
    {
        await using var c = new FbConnection(Cs("WIN1250"));
        await c.OpenAsync();
        Exec(c, null, $"DELETE FROM {table}");

        var sw = Stopwatch.StartNew();
        var tx = Begin(c);
        using var cmd = c.CreateCommand();
        cmd.CommandText = string.Format(CultureInfo.InvariantCulture, InsertSql, table);
        cmd.Transaction = tx;
        AddRowParameters(cmd.Parameters, 0);
        cmd.Prepare();

        for (int i = 0; i < rows; i++)
        {
            var r = Row(i);
            cmd.Parameters[0].Value = r.Id;
            cmd.Parameters[1].Value = r.Code;
            cmd.Parameters[2].Value = r.Name;
            cmd.Parameters[3].Value = r.Qty;
            cmd.Parameters[4].Value = r.Price;
            cmd.Parameters[5].Value = r.D;
            cmd.ExecuteNonQuery();

            if (commitEvery > 0 && (i + 1) % commitEvery == 0)
            {
                tx.Commit();
                tx.Dispose();
                tx = Begin(c);
                cmd.Transaction = tx;
                cmd.Prepare();
            }
        }
        tx.Commit();
        tx.Dispose();
        sw.Stop();

        var persisted = CountRows(c, null, table);
        if (persisted != rows) Fail($"{table} row count", $"expected {rows}, got {persisted} — SILENT LOSS");
        return rows / sw.Elapsed.TotalSeconds;
    }

    var w1 = await PreparedLoop("IMP_PLAIN", PerfRows, 0);
    Info("W1  prepared + re-bind, 1 tx, no index", $"{w1,10:N0} rows/s  ({PerfRows:N0} rows)");

    var w1b = await PreparedLoop("IMP_KEYED", PerfRows, 0);
    Info("W1b prepared + re-bind, 1 tx, PK + index", $"{w1b,10:N0} rows/s  ({PerfRows:N0} rows)");

    var w2 = await PreparedLoop("IMP_PLAIN", PerfRows, 10_000);
    Info("W2  prepared, commit every 10 000", $"{w2,10:N0} rows/s");

    var w2b = await PreparedLoop("IMP_PLAIN", PerfRows, 1_000);
    Info("W2b prepared, commit every 1 000", $"{w2b,10:N0} rows/s");

    var w2c = await PreparedLoop("IMP_PLAIN", PerfRows, 100);
    Info("W2c prepared, commit every 100", $"{w2c,10:N0} rows/s");

    // W3 — the naive path: a fresh command, no Prepare, per row.
    double w3;
    {
        await using var c = new FbConnection(Cs("WIN1250"));
        await c.OpenAsync();
        Exec(c, null, "DELETE FROM IMP_PLAIN");
        var sw = Stopwatch.StartNew();
        using var tx = Begin(c);
        for (int i = 0; i < NaiveRows; i++)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = string.Format(CultureInfo.InvariantCulture, InsertSql, "IMP_PLAIN");
            cmd.Transaction = tx;
            AddRowParameters(cmd.Parameters, i);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        sw.Stop();
        w3 = NaiveRows / sw.Elapsed.TotalSeconds;
        Info("W3  naive: new command per row, no Prepare", $"{w3,10:N0} rows/s  ({NaiveRows:N0} rows)");
    }

    // W4 — FbBatchCommand, chunked.
    async Task<double> BatchLoop(int rows, int chunk)
    {
        await using var c = new FbConnection(Cs("WIN1250"));
        await c.OpenAsync();
        Exec(c, null, "DELETE FROM IMP_PLAIN");

        var sw = Stopwatch.StartNew();
        using var tx = Begin(c);
        int sent = 0;
        while (sent < rows)
        {
            await using var batch = new FbBatchCommand(
                string.Format(CultureInfo.InvariantCulture, InsertSql, "IMP_PLAIN"), c, tx);
            int take = Math.Min(chunk, rows - sent);
            for (int i = 0; i < take; i++) AddRowParameters(batch.AddBatchParameters(), sent + i);
            batch.ExecuteNonQuery();
            sent += take;
        }
        tx.Commit();
        sw.Stop();

        var persisted = CountRows(c, null, "IMP_PLAIN");
        if (persisted != rows) Fail("batch row count", $"expected {rows}, got {persisted} — SILENT LOSS");
        return rows / sw.Elapsed.TotalSeconds;
    }

    try
    {
        var w4 = await BatchLoop(PerfRows, 1_000);
        Info("W4  FbBatchCommand, chunks of 1 000, 1 tx", $"{w4,10:N0} rows/s");
        var w4b = await BatchLoop(PerfRows, 10_000);
        Info("W4b FbBatchCommand, chunks of 10 000, 1 tx", $"{w4b,10:N0} rows/s");
        Info("W4 verdict", $"batch/prepared speed ratio = {w4b / w1:N2}x");
    }
    catch (Exception ex)
    {
        Info("W4  FbBatchCommand", $"UNUSABLE — {ex.GetType().Name}: {ex.Message}");
    }

    // ── Phase B — does a batch identify the failing row? ────────────────────────────────────────────
    Section("Phase B — batch error attribution (the blocking question for R7)");
    {
        await using var c = new FbConnection(Cs("WIN1250"));
        await c.OpenAsync();

        await using (var probe = new FbBatchCommand(
            string.Format(CultureInfo.InvariantCulture, InsertSql, "IMP_ERR"), c))
        {
            Info("B0  BatchBufferSize default", probe.BatchBufferSize.ToString(CultureInfo.InvariantCulture));
            Info("B0  MultiError default", probe.MultiError.ToString());
        }

        // B1 — MultiError = false (default): one bad row among ErrorRows.
        Exec(c, null, "DELETE FROM IMP_ERR");
        using (var tx = Begin(c))
        {
            try
            {
                await using var batch = new FbBatchCommand(
                    string.Format(CultureInfo.InvariantCulture, InsertSql, "IMP_ERR"), c, tx)
                { MultiError = false };
                for (int i = 0; i < ErrorRows; i++)
                {
                    var ps = batch.AddBatchParameters();
                    AddRowParameters(ps, i);
                    if (i == BadRowIndex) ps[0].Value = DBNull.Value;   // ID is NOT NULL — server-side error
                }
                var result = batch.ExecuteNonQuery();
                Info("B1  MultiError=false", $"no throw; AllSuccess={result.AllSuccess}, Count={result.Count}");
                ReportItems("B1", result);
            }
            catch (Exception ex)
            {
                Info("B1  MultiError=false", $"threw {ex.GetType().Name} — row identity NOT available from the exception alone");
                Info("B1  message", First(ex.Message));
            }
            tx.Rollback();
        }

        // B2 — MultiError = true: the whole point is per-row attribution.
        Exec(c, null, "DELETE FROM IMP_ERR");
        using (var tx = Begin(c))
        {
            try
            {
                await using var batch = new FbBatchCommand(
                    string.Format(CultureInfo.InvariantCulture, InsertSql, "IMP_ERR"), c, tx)
                { MultiError = true };
                for (int i = 0; i < ErrorRows; i++)
                {
                    var ps = batch.AddBatchParameters();
                    AddRowParameters(ps, i);
                    if (i == BadRowIndex) ps[0].Value = DBNull.Value;
                }
                var result = batch.ExecuteNonQuery();
                Info("B2  MultiError=true", $"AllSuccess={result.AllSuccess}, Count={result.Count} (batch had {ErrorRows})");
                ReportItems("B2", result);

                var failedIdx = new List<int>();
                for (int i = 0; i < result.Count; i++) if (!result[i].IsSuccess) failedIdx.Add(i);

                if (result.Count == ErrorRows && failedIdx.Count == 1 && failedIdx[0] == BadRowIndex)
                    Pass("B2  row attribution", $"result index {failedIdx[0]} == the bad row's batch index — ALIGNED, 1:1 with the batch");
                else if (failedIdx.Count == 1)
                    Info("B2  row attribution", $"exactly one failure, but at result index {failedIdx[0]} while the bad row was {BadRowIndex} (Count={result.Count}) — NOT 1:1");
                else
                    Info("B2  row attribution", $"failed indices = [{string.Join(", ", failedIdx)}] — expected exactly one");

                var persisted = CountRows(c, tx, "IMP_ERR");
                Info("B2  persisted in tx", $"{persisted} of {ErrorRows} (good rows continue past the bad one: {(persisted == ErrorRows - 1 ? "YES" : "NO")})");
                tx.Commit();
            }
            catch (Exception ex)
            {
                Info("B2  MultiError=true", $"threw {ex.GetType().Name}: {First(ex.Message)}");
                tx.Rollback();
            }
        }

        // B3 — the prepared-loop baseline: attribution is trivially exact (we know the row we are on).
        Exec(c, null, "DELETE FROM IMP_ERR");
        {
            var sw = Stopwatch.StartNew();
            int written = 0, failed = 0; int firstFailedRow = -1;
            using var tx = Begin(c);
            using var cmd = c.CreateCommand();
            cmd.CommandText = string.Format(CultureInfo.InvariantCulture, InsertSql, "IMP_ERR");
            cmd.Transaction = tx;
            AddRowParameters(cmd.Parameters, 0);
            cmd.Prepare();
            for (int i = 0; i < ErrorRows; i++)
            {
                var r = Row(i);
                cmd.Parameters[0].Value = i == BadRowIndex ? DBNull.Value : r.Id;
                cmd.Parameters[1].Value = r.Code;
                cmd.Parameters[2].Value = r.Name;
                cmd.Parameters[3].Value = r.Qty;
                cmd.Parameters[4].Value = r.Price;
                cmd.Parameters[5].Value = r.D;
                try { cmd.ExecuteNonQuery(); written++; }
                catch (FbException) { failed++; if (firstFailedRow < 0) firstFailedRow = i; }
            }
            tx.Commit();
            sw.Stop();
            if (failed == 1 && firstFailedRow == BadRowIndex && written == ErrorRows - 1)
                Pass("B3  prepared loop, skip-bad-rows", $"exact attribution (row {firstFailedRow}); {written} written, transaction survived the error");
            else
                Fail("B3  prepared loop, skip-bad-rows", $"written={written}, failed={failed}, firstFailedRow={firstFailedRow}");
            Info("B3  cost with per-row try/catch", $"{ErrorRows / sw.Elapsed.TotalSeconds,10:N0} rows/s");
        }
    }

    // ── Phase K — the CommandLock: does per-chunk serialization cost anything? ───────────────────────
    // The app must hold FirebirdConnectionService's per-connection lock around each wire operation
    // (gotchas #98/#120/#236). The question the plan asks is whether taking it PER CHUNK (rather than per
    // row) is free. Measured, not derived: the same batch loop with and without a real SemaphoreSlim.
    Section("Phase K — CommandLock cost per chunk");
    {
        const int Chunk = 500;
        var gate = new SemaphoreSlim(1, 1);

        async Task<double> LockedBatchLoop(bool useLock)
        {
            await using var c = new FbConnection(Cs("WIN1250"));
            await c.OpenAsync();
            Exec(c, null, "DELETE FROM IMP_PLAIN");
            var sw = Stopwatch.StartNew();
            using var tx = Begin(c);
            int sent = 0;
            while (sent < PerfRows)
            {
                int take = Math.Min(Chunk, PerfRows - sent);
                if (useLock) await gate.WaitAsync();
                try
                {
                    await using var batch = new FbBatchCommand(
                        string.Format(CultureInfo.InvariantCulture, InsertSql, "IMP_PLAIN"), c, tx);
                    for (int i = 0; i < take; i++) AddRowParameters(batch.AddBatchParameters(), sent + i);
                    batch.ExecuteNonQuery();
                }
                finally { if (useLock) gate.Release(); }
                sent += take;
            }
            tx.Commit();
            sw.Stop();
            return PerfRows / sw.Elapsed.TotalSeconds;
        }

        var free = await LockedBatchLoop(false);
        var locked = await LockedBatchLoop(true);
        Info("K1  batch (chunk 500), no lock", $"{free,10:N0} rows/s");
        Info("K2  batch (chunk 500), SemaphoreSlim per chunk", $"{locked,10:N0} rows/s");
        Info("K3  lock overhead", $"{(free - locked) / free * 100:N2}% — {PerfRows / Chunk} acquire/release pairs for {PerfRows:N0} rows");
    }

    // ── Phase S — batch chunk-size sweep (we must pick a default) ────────────────────────────────────
    Section("Phase S — batch chunk size sweep");
    foreach (var chunk in new[] { 100, 250, 500, 1_000, 2_000, 5_000, 20_000 })
    {
        try
        {
            var rate = await BatchLoop(PerfRows, chunk);
            Info($"S  chunk = {chunk,6:N0}", $"{rate,10:N0} rows/s");
        }
        catch (Exception ex)
        {
            Info($"S  chunk = {chunk,6:N0}", $"FAILED — {ex.GetType().Name}: {First(ex.Message)}");
        }
    }

    // ── Phase L — does a BATCH accept a BLOB? (ColumnTypeInferencer can produce BLOB SUB_TYPE TEXT) ──
    Section("Phase L — BLOB through the batch API");
    {
        await using var c = new FbConnection(Cs("WIN1250"));
        await c.OpenAsync();
        Exec(c, null, "CREATE TABLE IMP_BLOB (ID INTEGER, T BLOB SUB_TYPE TEXT)");
        var big = new string('A', 20_000);

        using (var tx = Begin(c))
        {
            try
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "INSERT INTO IMP_BLOB (ID, T) VALUES (@id, @t)";
                cmd.Transaction = tx;
                cmd.Parameters.Add(new FbParameter("@id", FbDbType.Integer) { Value = 1 });
                cmd.Parameters.Add(new FbParameter("@t", FbDbType.Text) { Value = big });
                cmd.ExecuteNonQuery();
                var len = Convert.ToInt64(Scalar(c, tx, "SELECT CHAR_LENGTH(T) FROM IMP_BLOB WHERE ID = 1") ?? 0L, CultureInfo.InvariantCulture);
                if (len == big.Length) Pass("L1  BLOB via prepared command", $"{len:N0} chars round-tripped");
                else Fail("L1  BLOB via prepared command", $"stored {len} of {big.Length} chars");
            }
            catch (Exception ex) { Fail("L1  BLOB via prepared command", $"{ex.GetType().Name}: {First(ex.Message)}"); }
            tx.Commit();
        }

        using (var tx = Begin(c))
        {
            try
            {
                await using var batch = new FbBatchCommand("INSERT INTO IMP_BLOB (ID, T) VALUES (@id, @t)", c, tx);
                for (int i = 2; i <= 4; i++)
                {
                    var ps = batch.AddBatchParameters();
                    ps.Add(new FbParameter("@id", FbDbType.Integer) { Value = i });
                    ps.Add(new FbParameter("@t", FbDbType.Text) { Value = big });
                }
                var res = batch.ExecuteNonQuery();
                var len = Convert.ToInt64(Scalar(c, tx, "SELECT CHAR_LENGTH(T) FROM IMP_BLOB WHERE ID = 2") ?? 0L, CultureInfo.InvariantCulture);
                if (res.AllSuccess && len == big.Length)
                    Pass("L2  BLOB via FbBatchCommand", $"accepted, {len:N0} chars round-tripped");
                else
                    Info("L2  BLOB via FbBatchCommand", $"AllSuccess={res.AllSuccess}, stored {len} of {big.Length} chars");
            }
            catch (Exception ex)
            {
                Info("L2  BLOB via FbBatchCommand", $"UNSUPPORTED — {ex.GetType().Name}: {First(ex.Message)} ⇒ the writer must fall back to the prepared loop when a BLOB is mapped");
            }
            tx.Rollback();
        }
    }

    // ── Phase E — error identity: SQLSTATE + GDS codes (never message text) ──────────────────────────
    Section("Phase E — error codes for the ImportErrorKind mapping (codes, never text)");
    {
        await using var c = new FbConnection(Cs("WIN1250"));
        await c.OpenAsync();
        Exec(c, null, """
            CREATE TABLE IMP_CONSTR (
              ID INTEGER NOT NULL PRIMARY KEY,
              CODE VARCHAR(10) NOT NULL,
              QTY INTEGER CHECK (QTY >= 0),
              SMALL_N SMALLINT,
              U VARCHAR(10) UNIQUE)
            """);
        Exec(c, null, "CREATE TABLE IMP_PARENT (ID INTEGER NOT NULL PRIMARY KEY)");
        Exec(c, null, """
            CREATE TABLE IMP_CHILD (
              ID INTEGER NOT NULL PRIMARY KEY,
              PARENT_ID INTEGER REFERENCES IMP_PARENT (ID))
            """);
        using (var seed = Begin(c))
        {
            Exec(c, seed, "INSERT INTO IMP_CONSTR (ID, CODE, QTY, U) VALUES (1, 'A', 1, 'DUP')");
            seed.Commit();
        }

        void CodeCase(string tag, string sql, string expectation)
        {
            using var tx = Begin(c);
            try
            {
                Exec(c, tx, sql);
                Info($"E  {tag}", $"NO ERROR — {expectation} did not fail (check the assumption!)");
                tx.Rollback();
            }
            catch (FbException ex)
            {
                var gds = new List<string>();
                foreach (FbError e in ex.Errors)
                    gds.Add(e.Number.ToString(CultureInfo.InvariantCulture));
                Info($"E  {tag}", $"SQLSTATE={ex.SQLSTATE} ErrorCode={ex.ErrorCode} GDS=[{string.Join(",", gds)}] — {expectation}");
                try { tx.Rollback(); } catch { /* already gone */ }
            }
            catch (Exception ex)
            {
                Info($"E  {tag}", $"{ex.GetType().Name} (CLIENT-side, no server round trip): {First(ex.Message)} — {expectation}");
                try { tx.Rollback(); } catch { /* already gone */ }
            }
        }

        CodeCase("NOT NULL", "INSERT INTO IMP_CONSTR (ID, CODE) VALUES (2, NULL)", "NOT NULL violation");
        CodeCase("PK dup", "INSERT INTO IMP_CONSTR (ID, CODE) VALUES (1, 'B')", "primary key violation");
        CodeCase("UNIQUE dup", "INSERT INTO IMP_CONSTR (ID, CODE, U) VALUES (3, 'C', 'DUP')", "unique violation");
        CodeCase("CHECK", "INSERT INTO IMP_CONSTR (ID, CODE, QTY) VALUES (4, 'D', -5)", "check constraint");
        CodeCase("FK missing", "INSERT INTO IMP_CHILD (ID, PARENT_ID) VALUES (1, 999)", "foreign key violation");
        CodeCase("string too long", "INSERT INTO IMP_CONSTR (ID, CODE) VALUES (5, 'ABCDEFGHIJKLMNOP')", "VARCHAR(10) overflow");
        CodeCase("numeric overflow", "INSERT INTO IMP_CONSTR (ID, CODE, SMALL_N) VALUES (6, 'E', 99999)", "SMALLINT overflow");
        CodeCase("transliteration", "INSERT INTO IMP_CS (TAG, C_1250) VALUES ('E-TRANS', _UTF8 x'D096')", "UTF8 'Ж' into a WIN1250 column");
    }

    // ── Phase T — over-long string ───────────────────────────────────────────────────────────────────
    Section("Phase T — over-long string: error or silent truncation? (§0.2)");
    {
        await using var c = new FbConnection(Cs("WIN1250"));
        await c.OpenAsync();
        using var tx = Begin(c);
        var tooLong = new string('X', 40);   // column is VARCHAR(20)
        try
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO IMP_TRUNC (C) VALUES (@c)";
            cmd.Transaction = tx;
            cmd.Parameters.Add(new FbParameter("@c", FbDbType.VarChar) { Value = tooLong });
            cmd.ExecuteNonQuery();
            var stored = Scalar(c, tx, "SELECT C FROM IMP_TRUNC") as string;
            if (stored is null || stored.Length == tooLong.Length)
                Info("T1  40 chars into VARCHAR(20)", $"accepted, stored length {stored?.Length}");
            else
                Fail("T1  40 chars into VARCHAR(20)", $"SILENTLY TRUNCATED to {stored.Length} chars — a §0.2 data-loss vector");
        }
        catch (Exception ex)
        {
            Pass("T1  40 chars into VARCHAR(20)", $"rejected ({ex.GetType().Name}) — no silent truncation. {First(ex.Message)}");
        }
        tx.Rollback();
    }

    // ── Phase C — connection charset (risk R1) ───────────────────────────────────────────────────────
    Section("Phase C — connection charset: failure or silent substitution? (risk R1)");
    {
        const string Cyrillic = "Ж";      // not representable in WIN1250
        const string Cjk = "中";           // not representable in WIN1250
        const string Euro = "€";          // IS representable in WIN1250 (0x80)
        const string Polish = "ąćęłńóśźż";// representable in WIN1250

        void CharsetCase(string tag, string connCharset, string column, string value, string expectation)
        {
            try
            {
                using var c = new FbConnection(Cs(connCharset));
                c.Open();
                using var tx = Begin(c);
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"INSERT INTO IMP_CS (TAG, {column}) VALUES (@t, @v)";
                cmd.Transaction = tx;
                cmd.Parameters.Add(new FbParameter("@t", FbDbType.VarChar) { Value = tag });
                cmd.Parameters.Add(new FbParameter("@v", FbDbType.VarChar) { Value = value });
                cmd.ExecuteNonQuery();
                tx.Commit();

                // Read back through a UTF8 connection — the only charset that can represent everything,
                // so what comes back is what was actually STORED, not what the writer's charset allows.
                using var r = new FbConnection(Cs("UTF8"));
                r.Open();
                var stored = Scalar(r, null, $"SELECT {column} FROM IMP_CS WHERE TAG = '{tag}'") as string;

                if (string.Equals(stored, value, StringComparison.Ordinal))
                    Pass($"C  {tag}", $"conn={connCharset} col={column}: round-tripped intact ({expectation})");
                else
                    Fail($"C  {tag}", $"conn={connCharset} col={column}: SILENT SUBSTITUTION — wrote '{value}', stored '{stored}' ({expectation})");
            }
            catch (Exception ex)
            {
                Pass($"C  {tag}", $"conn={connCharset} col={column}: REJECTED at {(ex is FbException ? "server/driver (FbException)" : ex.GetType().Name)} — {First(ex.Message)}");
            }
        }

        CharsetCase("C1", "WIN1250", "C_1250", Cyrillic, "unrepresentable in the connection charset");
        CharsetCase("C2", "WIN1250", "C_UTF8", Cyrillic, "column could hold it; the CONNECTION cannot carry it");
        CharsetCase("C2b", "WIN1250", "C_UTF8", Cjk, "same, CJK");
        CharsetCase("C3", "UTF8", "C_UTF8", Cyrillic, "should succeed");
        CharsetCase("C4", "UTF8", "C_1250", Cyrillic, "server-side transliteration into WIN1250");
        CharsetCase("C5", "WIN1250", "C_1250", Polish, "control: WIN1250 handles Polish");
        CharsetCase("C6", "WIN1250", "C_1250", Euro, "control: € is in WIN1250 (0x80)");
        CharsetCase("C7", "UTF8", "C_1250", Euro, "€ across UTF8 → WIN1250 column");
    }
}
finally
{
    // ── Cleanup — the scratch DB never outlives the probe ────────────────────────────────────────────
    Section("Cleanup");
    try
    {
        FbConnection.ClearAllPools();
        FbConnection.DropDatabase(Cs("WIN1250"));
        Console.WriteLine($"  ....  dropped {ScratchPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ....  could not drop {ScratchPath} — {ex.Message} (delete by hand)");
    }
}

Console.WriteLine();
Console.WriteLine(failures == 0
    ? "ALL CHECKS OK (informational '....' lines carry the measurements)"
    : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

// ── helpers ─────────────────────────────────────────────────────────────────────────────────────────
static string First(string s)
{
    var line = s.Split('\n')[0].Trim();
    return line.Length > 160 ? line[..160] + "…" : line;
}

void ReportItems(string tag, FbBatchNonQueryResult result)
{
    int ok = 0, bad = 0; string firstError = "";
    for (int i = 0; i < result.Count; i++)
    {
        if (result[i].IsSuccess) ok++;
        else
        {
            bad++;
            if (firstError.Length == 0)
                firstError = $"idx {i}: {result[i].Exception?.GetType().Name}: {First(result[i].Exception?.Message ?? "(no exception object)")}";
        }
    }
    Info($"{tag}  items", $"success={ok}, failed={bad}" + (firstError.Length > 0 ? $" | first → {firstError}" : ""));
}
