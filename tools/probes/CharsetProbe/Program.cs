using System.Globalization;
using System.Text;
using EmberTern.Core.Connections;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;

// ─────────────────────────────────────────────────────────────────────────────────────────────────
// Phase 5 — charset guard: live verification of the FIVE production paths.
//
// The hermetic suite proves the ORACLE (what we refuse == what the driver would damage). This proves the
// WIRING: that each real path actually runs through the shared seam, and that a refusal happens BEFORE the
// driver encodes anything.
//
//   1. bound parameter      — FirebirdQueryExecutor
//   2. SQL literal / F5     — FirebirdQueryExecutor
//   3. DDL / source         — FirebirdDdlExecutor   ⭐ + proof the stored source is untouched
//   4. import               — FirebirdImportWriter + ImportRowValidator
//   5. debugger             — DebugSession over FirebirdDebugExecutor (draft source)
//
// Every check is run twice: once with an unrepresentable character (must be REFUSED, nothing written) and
// once with ordinary Polish text (must SUCCEED — a guard that blocks valid work is a failed guard).
// ─────────────────────────────────────────────────────────────────────────────────────────────────

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

var pwd = Environment.GetEnvironmentVariable("ET_LAB_PWD");
if (string.IsNullOrWhiteSpace(pwd))
{
    Console.WriteLine("Set ET_LAB_PWD to the local dev SYSDBA password.");
    return 2;
}

// Work on a COPY of the lab DB: the probe creates and drops objects, and Lab/EmberTern_Lab.fdb is committed.
string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
string labPath = Path.Combine(repo, "Lab", "EmberTern_Lab.fdb");
if (!File.Exists(labPath)) { Console.WriteLine($"Lab DB not found at {labPath}"); return 2; }

string workPath = Path.Combine(Path.GetTempPath(), "EmberTern_CharsetProbe.fdb");
File.Copy(labPath, workPath, overwrite: true);
Console.WriteLine($"Working copy: {workPath}\n");

const string Bad = "Ж";                       // -> '?'      under WIN1250
const string BadBestFit = "£";                // -> 'L'      under WIN1250 — the silent, plausible class
const string Good = "Zażółć gęślą jaźń";      // fully representable in WIN1250

int failures = 0;
void Pass(string what, string detail = "") => Console.WriteLine($"  PASS  {what}{(detail.Length > 0 ? "  — " + detail : "")}");
void Fail(string what, string detail) { failures++; Console.WriteLine($"  FAIL  {what}  — {detail}"); }
void Head(string t) => Console.WriteLine($"\n=== {t} ===");

var observerCs = new FbConnectionStringBuilder
{
    Database = workPath, DataSource = "localhost", Port = 3050, UserID = "SYSDBA",
    Password = pwd, Charset = "UTF8", Dialect = 3, ServerType = FbServerType.Default, Pooling = false,
}.ToString();

async Task<string?> ObserveAsync(string sql)
{
    await using var cn = new FbConnection(observerCs);
    await cn.OpenAsync();
    await using var cmd = new FbCommand(sql, cn);
    var v = await cmd.ExecuteScalarAsync();
    return v is null or DBNull ? null : v.ToString();
}

async Task ObserverExecAsync(string sql)
{
    await using var cn = new FbConnection(observerCs);
    await cn.OpenAsync();
    await using var cmd = new FbCommand(sql, cn);
    await cmd.ExecuteNonQueryAsync();
}

/// <summary>True when the failure is the charset guard's refusal (whatever domain exception carried it).</summary>
static bool IsCharsetRefusal(Exception ex)
{
    for (Exception? e = ex; e is not null; e = e.InnerException)
    {
        if (e is CharsetRepresentationException) return true;
        if (e.Message.Contains("cannot represent", StringComparison.OrdinalIgnoreCase)) return true;
    }
    return false;
}

try
{
    try { await ObserverExecAsync("DROP TABLE CS_GUARD"); } catch { }
    await ObserverExecAsync(
        "CREATE TABLE CS_GUARD (ID INTEGER NOT NULL PRIMARY KEY, TXT VARCHAR(200) CHARACTER SET UTF8)");

    var profile = new ConnectionProfile
    {
        Name = "charset-guard-probe", Host = "localhost", Port = 3050, DatabasePath = workPath,
        Username = "SYSDBA", Password = pwd, Charset = "WIN1250", Dialect = 3,
    };

    using var service = new FirebirdConnectionService();
    await service.ConnectAsync(profile);
    Console.WriteLine($"Connected on WIN1250 (the vulnerable default). DdlIsIndependent={service.DdlIsIndependent}");

    var tx = new TransactionService(service);
    var exec = new FirebirdQueryExecutor(service, tx);
    var ddl = new FirebirdDdlExecutor(service, tx);

    // ── 1. BOUND PARAMETER ────────────────────────────────────────────────────────────────────────
    Head("1. BOUND PARAMETER  (FirebirdQueryExecutor)");
    foreach (var (label, value, id) in new[] { ("'?' class", Bad, 1), ("best-fit class", BadBestFit, 2) })
    {
        try
        {
            await exec.ExecuteAsync(
                $"INSERT INTO CS_GUARD (ID, TXT) VALUES ({id}, @p)",
                new[] { new QueryParameter("@p", value) });
            await tx.CommitAsync();
            var stored = await ObserveAsync($"SELECT TXT FROM CS_GUARD WHERE ID = {id}");
            Fail($"param {label}", $"NOT refused — stored \"{stored}\" (sent \"{value}\")");
        }
        catch (Exception ex) when (IsCharsetRefusal(ex))
        {
            try { await tx.RollbackAsync(); } catch { }
            var stored = await ObserveAsync($"SELECT TXT FROM CS_GUARD WHERE ID = {id}");
            if (stored is null) Pass($"param {label}", "refused, nothing written");
            else Fail($"param {label}", $"refused BUT a row exists: \"{stored}\"");
        }
    }

    try
    {
        await exec.ExecuteAsync(
            "INSERT INTO CS_GUARD (ID, TXT) VALUES (3, @p)", new[] { new QueryParameter("@p", Good) });
        await tx.CommitAsync();
        var stored = await ObserveAsync("SELECT TXT FROM CS_GUARD WHERE ID = 3");
        if (stored == Good) Pass("param representable", $"stored intact: \"{stored}\"");
        else Fail("param representable", $"stored \"{stored}\"");
    }
    catch (Exception ex) { Fail("param representable", $"BLOCKED valid work: {ex.GetType().Name}: {Short(ex.Message)}"); }

    // ── 2. SQL LITERAL / F5 ───────────────────────────────────────────────────────────────────────
    Head("2. SQL LITERAL / F5  (FirebirdQueryExecutor)");
    foreach (var (label, value, id) in new[] { ("'?' class", Bad, 11), ("best-fit class", BadBestFit, 12) })
    {
        try
        {
            await exec.ExecuteAsync($"INSERT INTO CS_GUARD (ID, TXT) VALUES ({id}, '{value}')");
            await tx.CommitAsync();
            var stored = await ObserveAsync($"SELECT TXT FROM CS_GUARD WHERE ID = {id}");
            Fail($"literal {label}", $"NOT refused — stored \"{stored}\"");
        }
        catch (Exception ex) when (IsCharsetRefusal(ex))
        {
            try { await tx.RollbackAsync(); } catch { }
            var stored = await ObserveAsync($"SELECT TXT FROM CS_GUARD WHERE ID = {id}");
            if (stored is null) Pass($"literal {label}", "refused, nothing written");
            else Fail($"literal {label}", $"refused BUT a row exists: \"{stored}\"");
        }
    }

    try
    {
        await exec.ExecuteAsync($"INSERT INTO CS_GUARD (ID, TXT) VALUES (13, '{Good}')");
        await tx.CommitAsync();
        var stored = await ObserveAsync("SELECT TXT FROM CS_GUARD WHERE ID = 13");
        if (stored == Good) Pass("literal representable", $"stored intact: \"{stored}\"");
        else Fail("literal representable", $"stored \"{stored}\"");
    }
    catch (Exception ex) { Fail("literal representable", $"BLOCKED valid work: {ex.GetType().Name}: {Short(ex.Message)}"); }

    // ── 3. DDL / SOURCE  ⭐ the rule #11 path ──────────────────────────────────────────────────────
    Head("3. DDL / SOURCE  (FirebirdDdlExecutor)  ⭐ rule #11");

    const string Proc = "SP_CS_GUARD";
    string Body(string marker) =>
        $"CREATE OR ALTER PROCEDURE {Proc} RETURNS (R VARCHAR(200) CHARACTER SET UTF8) AS\n" +
        $"BEGIN\n  R = '{marker}';\n  SUSPEND;\nEND";

    // Establish a KNOWN-GOOD baseline the guard must not disturb.
    await ddl.ExecuteAsync(Body(Good));
    var baseline = await ObserveAsync(
        $"SELECT RDB$PROCEDURE_SOURCE FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = '{Proc}'");
    if (baseline is not null && baseline.Contains(Good, StringComparison.Ordinal))
        Pass("DDL representable", "compiled, source stored verbatim");
    else
        Fail("DDL representable", $"BLOCKED or altered valid work; source = {Short(baseline ?? "<null>")}");

    foreach (var (label, marker) in new[] { ("'?' class", Bad), ("best-fit class", BadBestFit) })
    {
        try
        {
            await ddl.ExecuteAsync(Body(marker));
            var after = await ObserveAsync(
                $"SELECT RDB$PROCEDURE_SOURCE FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = '{Proc}'");
            Fail($"DDL {label}", $"NOT refused — stored source now: {Short(after ?? "<null>")}");
        }
        catch (Exception ex) when (IsCharsetRefusal(ex))
        {
            // ⭐ The check the user asked for explicitly: not merely "it threw", but that the stored source is
            // BYTE-IDENTICAL to what it was before. A refusal that still rewrote the object would be worthless.
            var after = await ObserveAsync(
                $"SELECT RDB$PROCEDURE_SOURCE FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = '{Proc}'");
            if (string.Equals(after, baseline, StringComparison.Ordinal))
                Pass($"DDL {label}", "refused; stored source BYTE-IDENTICAL to before");
            else
                Fail($"DDL {label}", $"refused BUT the stored source changed:\n        was: {Short(baseline)}\n        now: {Short(after ?? "<null>")}");
        }
        catch (Exception ex)
        {
            Fail($"DDL {label}", $"failed for the wrong reason: {ex.GetType().Name}: {Short(ex.Message)}");
        }
    }

    // ── 4. IMPORT ─────────────────────────────────────────────────────────────────────────────────
    Head("4. IMPORT  (ImportRowValidator + FirebirdImportWriter)");

    // 4a. The module's own contract: one failed ROW carrying its source row number — not an aborted run.
    var strict = ImportCharsetGuard.Strict(profile.Charset);
    var textColumn = new ColumnSpec("TXT", "VARCHAR(200)");
    var verdict = ImportRowValidator.Validate(Bad, textColumn, new ImportBehaviorOptions(), strict, rawText: Bad);
    if (!verdict.IsSuccess && verdict.Kind == ImportErrorKind.NotRepresentableInConnectionCharset)
        Pass("import validator", "refused as NotRepresentableInConnectionCharset (contract unchanged)");
    else
        Fail("import validator", $"ok={verdict.IsSuccess} kind={verdict.Kind}");

    // 4b. The NONE hole this phase closed — the shipped guard used to say "fits".
    if (!ImportCharsetGuard.CanRepresent(Bad, ImportCharsetGuard.Strict("NONE")))
        Pass("import NONE hole", "a NONE connection no longer claims everything fits");
    else
        Fail("import NONE hole", "NONE still resolves to UTF8 — the hole is open");

    // 4c. The writer-level backstop, on the real batched writer and the module's own attachment.
    var importSession = await service.CreateImportSessionAsync();
    try
    {
        var writer = new FirebirdImportWriter(importSession, ImportErrorPolicy.StopOnFirstError);
        await writer.BeginAsync(
            new ImportTarget("CS_GUARD", new[] { textColumn }, Array.Empty<string>()),
            new[] { new ColumnMapping { TargetColumnName = "TXT", SourceFieldIndex = 0 } },
            CancellationToken.None);

        try
        {
            await writer.WriteAsync(new ImportRow(1, new object?[] { Bad }), CancellationToken.None);
            Fail("import writer backstop", "NOT refused");
        }
        catch (Exception ex) when (IsCharsetRefusal(ex))
        {
            Pass("import writer backstop", "refused before the batch was sent");
        }
    }
    finally
    {
        try { await importSession.RollbackAsync(); } catch { }
        await importSession.DisposeAsync();
    }

    // ── 5. DEBUGGER ───────────────────────────────────────────────────────────────────────────────
    Head("5. DEBUGGER  (DebugSession over FirebirdDebugExecutor)");

    // The realistic case is the DRAFT model: the user edits the routine in the editor, adds a character the
    // connection cannot carry, and presses Debug — the session runs from the EDITED text without saving. If
    // the guard missed this, the debugger would execute code that differs from what is on screen, which the
    // fidelity law (§F) forbids outright.
    var fallback = CharsetCatalog.Resolve(profile.Charset);

    async Task<(bool Started, string Detail)> TryDebugAsync(string marker)
    {
        var draft =
            $"CREATE OR ALTER PROCEDURE {Proc} RETURNS (R VARCHAR(200) CHARACTER SET UTF8) AS\n" +
            $"BEGIN\n  R = '{marker}';\n  SUSPEND;\nEND";

        var model = SemanticModel.Build(SqlParser.Parse(draft).Root);
        var body = model.Syntax.Statements.OfType<DdlStatement>().First(d => d.Body is not null).Body!;

        var session = await service.CreateDebugSessionAsync(DebugIsolation.ReadCommitted);
        try
        {
            var executor = await FirebirdDebugExecutor.CreateAsync(session, Proc, draft, body, model, fallback);
            var dbg = new DebugSession(body, executor, Proc, new Dictionary<string, object?>(), draft, model);
            dbg.Start();

            var guard = 0;
            while (dbg.State == DebugState.Paused && guard++ < 50) dbg.Step(StepKind.Over);

            return (true, $"ran to {dbg.State}");
        }
        finally
        {
            try { await session.DisposeAsync(); } catch { }
        }
    }

    foreach (var (label, marker) in new[] { ("'?' class", Bad), ("best-fit class", BadBestFit) })
    {
        try
        {
            var (started, detail) = await TryDebugAsync(marker);
            if (started) Fail($"debugger {label}", $"session executed unrepresentable code — {detail}");
            else Fail($"debugger {label}", detail);
        }
        catch (Exception ex) when (IsCharsetRefusal(ex))
        {
            Pass($"debugger {label}", "session could not execute the draft — refused before the driver");
        }
        catch (Exception ex)
        {
            Fail($"debugger {label}", $"failed for the wrong reason: {ex.GetType().Name}: {Short(ex.Message)}");
        }
    }

    try
    {
        var (started, detail) = await TryDebugAsync(Good);
        if (started) Pass("debugger representable", detail);
        else Fail("debugger representable", detail);
    }
    catch (Exception ex)
    {
        Fail("debugger representable", $"BLOCKED valid work: {ex.GetType().Name}: {Short(ex.Message)}");
    }

    await service.DisconnectAsync();
}
finally
{
    Head("CLEANUP");
    foreach (var s in new[] { "DROP PROCEDURE SP_CS_GUARD", "DROP TABLE CS_GUARD" })
    {
        try { await ObserverExecAsync(s); Console.WriteLine($"  dropped: {s}"); }
        catch (Exception ex) { Console.WriteLine($"  skipped ({s}): {Short(ex.Message)}"); }
    }
    try { File.Delete(workPath); Console.WriteLine("  removed working copy"); } catch { }
}

Console.WriteLine($"\n=== FAILURES: {failures} ===");
return failures == 0 ? 0 : 1;

static string Short(string s)
{
    var flat = s.Replace('\n', ' ').Replace('\r', ' ');
    return flat.Length <= 140 ? flat : flat[..140] + "…";
}
