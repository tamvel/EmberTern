// Domain-typed routine signatures (S-1b) — live verification probe.
// See DomainSignatureProbe.csproj for what this proves and why the unit tests cannot.
//
//   $env:ET_LAB_PWD = "<sysdba password>"
//   dotnet run --project tools/probes/DomainSignatureProbe
//
// Requires the local FB5 DefaultInstance on localhost:3050. Creates and drops its OWN scratch database at
// an ASCII path (gotcha #149) — the lab database is never touched.

using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;

// WIN1250 is not a .NET-built-in encoding; the bare FbConnection used to build the scratch schema needs the
// provider registered before it opens (FirebirdConnectionService does this in its own static ctor).
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var password = Environment.GetEnvironmentVariable("ET_LAB_PWD");
if (string.IsNullOrEmpty(password))
{
    Console.Error.WriteLine("Set ET_LAB_PWD to the SYSDBA password for this shell (see tools/probes/README.md).");
    return 2;
}

var scratchPath = @"C:\Temp\embertern_domainsig.fdb";
Directory.CreateDirectory(@"C:\Temp");

Console.WriteLine("Domain-typed routine signatures (S-1b) — live verification");
Console.WriteLine($"Scratch database: {scratchPath}");
Console.WriteLine();

var csb = new FbConnectionStringBuilder
{
    DataSource = "localhost",
    Port = 3050,
    Database = scratchPath,
    UserID = "SYSDBA",
    Password = password,
    Charset = "WIN1250",
    Dialect = 3,
    ServerType = FbServerType.Default,
};

var failures = 0;
void Check(string what, bool ok, string detail = "")
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail.Length > 0 ? " — " + detail : "")}");
    if (!ok) failures++;
}

// ── Build the scratch schema ────────────────────────────────────────────────────────────────────────────
// Mirrors the shapes added to Lab/setup.sql, so the two describe the same case: an input domain param, an
// output domain param, a plain param beside them (the anonymous-backing-domain case), a domain param with a
// DEFAULT, a param on a NOT NULL domain, an explicit NOT NULL on a nullable domain, and a function with a
// domain argument AND a domain RETURNS.
FbConnection.CreateDatabase(csb.ToString(), overwrite: true);
await using (var raw = new FbConnection(csb.ToString()))
{
    await raw.OpenAsync();
    await ExecAsync(raw, "CREATE DOMAIN D_CODE AS CHAR(8)");
    await ExecAsync(raw, "CREATE DOMAIN D_QTY AS INTEGER DEFAULT 1 CHECK (VALUE > 0)");
    await ExecAsync(raw, "CREATE DOMAIN D_NAME AS VARCHAR(60) NOT NULL");
    await ExecAsync(raw,
        "CREATE OR ALTER PROCEDURE SP_DOM (P_CODE D_CODE, P_PLAIN INTEGER, P_NN D_NAME, "
        + "P_EXPL D_CODE NOT NULL, P_QTY D_QTY = 5) "
        + "RETURNS (R_CODE D_CODE, R_TOTAL NUMERIC(15,2)) "
        + "AS BEGIN R_CODE = P_CODE; R_TOTAL = P_QTY; SUSPEND; END");
    await ExecAsync(raw,
        "CREATE OR ALTER FUNCTION FN_DOM (P_CODE D_CODE) RETURNS D_NAME "
        + "AS BEGIN RETURN COALESCE(TRIM(P_CODE), 'NONE'); END");
}
Console.WriteLine("Scratch schema built (3 domains, SP_DOM, FN_DOM).");
Console.WriteLine();

var profile = new ConnectionProfile
{
    Name = "domainsig",
    Host = "localhost",
    Port = 3050,
    DatabasePath = scratchPath,
    Username = "SYSDBA",
    Password = password,
    Charset = "WIN1250",
    Dialect = 3,
};

using var service = new FirebirdConnectionService();
await service.ConnectAsync(profile);
var transactionService = new TransactionService(service);
var lane = new MetadataLane(service, transactionService);
var ddlReader = new FirebirdDdlReader(service, lane);
var detailReader = new FirebirdTableDetailReader(service, lane, transactionService);

Console.WriteLine($"Connected. Server: {service.RequireOpenConnection().ServerVersion}");
Console.WriteLine();

var procedure = new MetadataObject("SP_DOM", MetadataObjectKind.Procedure);
var function = new MetadataObject("FN_DOM", MetadataObjectKind.Function);

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
// CLAIM 1 — the DOMAIN survives the read, and a plain parameter still shows its base type.
// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Console.WriteLine("── CLAIM 1: the reconstruction names the domain, not the base type ──────────────────────");

var procSource = await ddlReader.FetchProcedureSourceAsync(procedure);
Console.WriteLine("  ── reconstructed CREATE OR ALTER PROCEDURE ──");
foreach (var line in procSource.Split('\n').Take(12)) Console.WriteLine("    " + line.TrimEnd());
Console.WriteLine();

Check("input parameter on a domain shows D_CODE", procSource.Contains("P_CODE D_CODE", StringComparison.Ordinal));
Check("OUTPUT parameter on a domain shows D_CODE", procSource.Contains("R_CODE D_CODE", StringComparison.Ordinal));
Check("domain parameter with a DEFAULT shows D_QTY", procSource.Contains("P_QTY D_QTY", StringComparison.Ordinal));
Check("a PLAIN parameter beside them still shows its base type",
    procSource.Contains("P_PLAIN INTEGER", StringComparison.Ordinal));
Check("a plain OUTPUT parameter still shows its base type",
    procSource.Contains("R_TOTAL NUMERIC(15,2)", StringComparison.Ordinal));
Check("no anonymous backing domain leaked into the text", !procSource.Contains("RDB$", StringComparison.Ordinal));

var funcSource = await ddlReader.FetchFunctionSourceAsync(function);
Console.WriteLine("  ── reconstructed CREATE OR ALTER FUNCTION ──");
foreach (var line in funcSource.Split('\n').Take(8)) Console.WriteLine("    " + line.TrimEnd());
Console.WriteLine();

Check("function ARGUMENT on a domain shows D_CODE", funcSource.Contains("P_CODE D_CODE", StringComparison.Ordinal));
Check("function RETURNS on a domain shows D_NAME", funcSource.Contains("RETURNS D_NAME", StringComparison.Ordinal));
Console.WriteLine();

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
// CLAIM 2 — nullability is not INVENTED and not LOST.
//
// Measured on FB5 before the fix was written: for `P_NN D_NAME` (the domain is itself NOT NULL) the
// parameter's own RDB$NULL_FLAG is NULL and the domain's is 1; for `P_EXPL D_CODE NOT NULL` it is the other
// way round. So the emitted NOT NULL must follow the emitted TYPE, or the reconstruction either adds a
// clause the original never had or drops one it did.
// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Console.WriteLine("── CLAIM 2: NOT NULL follows the type source ────────────────────────────────────────────");

Check("a param on a NOT NULL domain does NOT gain an explicit NOT NULL",
    procSource.Contains("P_NN D_NAME", StringComparison.Ordinal)
    && !procSource.Contains("P_NN D_NAME NOT NULL", StringComparison.Ordinal));
Check("a param that DECLARED NOT NULL on a nullable domain keeps it",
    procSource.Contains("P_EXPL D_CODE NOT NULL", StringComparison.Ordinal));
Console.WriteLine();

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
// CLAIM 3 — THE ROUND TRIP IS LOSSLESS. This is the claim that covers the reported defect.
//
// Recompiling the reconstruction is exactly what an object editor's Compile does. If reading → compiling →
// reading again is byte-identical, then "open a procedure and press Compile" can no longer change the
// object. Before the fix this FAILED: the second read came back with base types where the first had
// domains, i.e. the compile had rewritten the signature.
// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Console.WriteLine("── CLAIM 3: read → compile → read is byte-identical ─────────────────────────────────────");

await using (var raw = new FbConnection(csb.ToString()))
{
    await raw.OpenAsync();
    await ExecAsync(raw, procSource);
    await ExecAsync(raw, funcSource);
}

var procAfter = await ddlReader.FetchProcedureSourceAsync(procedure);
var funcAfter = await ddlReader.FetchFunctionSourceAsync(function);

Check("procedure survives a recompile of its own reconstruction",
    string.Equals(procSource, procAfter, StringComparison.Ordinal),
    string.Equals(procSource, procAfter, StringComparison.Ordinal) ? "" : FirstDifference(procSource, procAfter));
Check("function survives a recompile of its own reconstruction",
    string.Equals(funcSource, funcAfter, StringComparison.Ordinal),
    string.Equals(funcSource, funcAfter, StringComparison.Ordinal) ? "" : FirstDifference(funcSource, funcAfter));

// And the domain link genuinely still exists in the catalog afterwards — the assertion the text comparison
// alone cannot make (identical text would also be consistent with both reads being wrong the same way).
await using (var raw = new FbConnection(csb.ToString()))
{
    await raw.OpenAsync();
    var stillDomain = await ScalarAsync(raw,
        "SELECT TRIM(RDB$FIELD_SOURCE) FROM RDB$PROCEDURE_PARAMETERS "
        + "WHERE RDB$PROCEDURE_NAME = 'SP_DOM' AND TRIM(RDB$PARAMETER_NAME) = 'P_CODE'");
    Check("the CATALOG still records D_CODE after the recompile", stillDomain == "D_CODE", stillDomain ?? "null");

    var fnStillDomain = await ScalarAsync(raw,
        "SELECT TRIM(a.RDB$FIELD_SOURCE) FROM RDB$FUNCTION_ARGUMENTS a JOIN RDB$FUNCTIONS f "
        + "ON f.RDB$FUNCTION_NAME = a.RDB$FUNCTION_NAME "
        + "WHERE a.RDB$FUNCTION_NAME = 'FN_DOM' AND a.RDB$ARGUMENT_POSITION = f.RDB$RETURN_ARGUMENT");
    Check("the CATALOG still records D_NAME on the function's RETURNS", fnStillDomain == "D_NAME", fnStillDomain ?? "null");
}
Console.WriteLine();

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
// CLAIM 4 — the GRID path carries the domain too, and keeps the base type beside it.
//
// The Easy-mode parameter grids load from GetProcedureParametersAsync / GetFunctionSignatureAsync, NOT from
// the reconstructed text, so they are a second read with the same hazard.
// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Console.WriteLine("── CLAIM 4: the grid path (ProcedureParameterInfo) carries the domain ───────────────────");

var inputs = await detailReader.GetProcedureParametersAsync("SP_DOM", 0);
var outputs = await detailReader.GetProcedureParametersAsync("SP_DOM", 1);
var pCode = inputs.FirstOrDefault(p => p.Name == "P_CODE");
var pPlain = inputs.FirstOrDefault(p => p.Name == "P_PLAIN");
var rCode = outputs.FirstOrDefault(p => p.Name == "R_CODE");

Check("input P_CODE reports Domain = D_CODE", pCode?.Domain == "D_CODE", pCode?.Domain ?? "null");
Check("input P_CODE still reports the resolved base type", pCode?.Type == "CHAR(8)", pCode?.Type ?? "null");
Check("plain P_PLAIN reports NO domain", pPlain is not null && pPlain.Domain is null, pPlain?.Domain ?? "null");
Check("output R_CODE reports Domain = D_CODE", rCode?.Domain == "D_CODE", rCode?.Domain ?? "null");

var sig = await detailReader.GetFunctionSignatureAsync("FN_DOM");
Check("function argument reports Domain = D_CODE",
    sig.Arguments.FirstOrDefault()?.Domain == "D_CODE", sig.Arguments.FirstOrDefault()?.Domain ?? "null");
Check("function reports ReturnDomain = D_NAME", sig.ReturnDomain == "D_NAME", sig.ReturnDomain ?? "null");
Check("function still reports the resolved base return type",
    sig.ReturnType == "VARCHAR(60)", sig.ReturnType);
Console.WriteLine();

// ⚠⚠ THE DEBUGGER IS DELIBERATELY NOT CHECKED HERE, and that is not an omission.
//
// FirebirdDebugMetadata resolves a domain-typed parameter to its BASE type on purpose (spec §3.4 R2: a
// value injected into a domain-constrained parameter would fail the domain's CHECK / NOT NULL on entry) —
// the exact opposite of what this probe asserts for the DDL reconstruction. Both needs are correct, and the
// two now share one IsUserDomain predicate, so the sharing does need verifying.
//
// It is verified by DebuggerFidelityProbe, which is the debugger's own standing authority (simulated ==
// real, 38 cases on FB5) — a second, weaker check here would be a second opinion about someone else's
// subject. FirebirdDebugMetadata is also internal, and widening its visibility for a probe would be paying
// an architectural price to duplicate a check that already exists.
//
//   dotnet run --project tools/probes/DebuggerFidelityProbe

// ── Teardown ────────────────────────────────────────────────────────────────────────────────────────────
await service.DisconnectAsync();
FbConnection.ClearAllPools();
try { File.Delete(scratchPath); } catch { /* a held handle is not a verification failure */ }

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

static async Task ExecAsync(FbConnection connection, string sql)
{
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    await cmd.ExecuteNonQueryAsync();
}

static async Task<string?> ScalarAsync(FbConnection connection, string sql)
{
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    var value = await cmd.ExecuteScalarAsync();
    return value?.ToString()?.Trim();
}

// A byte-identity failure is useless without saying WHERE, so report the first differing line pair.
static string FirstDifference(string a, string b)
{
    var la = a.Split('\n');
    var lb = b.Split('\n');
    for (var i = 0; i < Math.Max(la.Length, lb.Length); i++)
    {
        var x = i < la.Length ? la[i].TrimEnd() : "<missing>";
        var y = i < lb.Length ? lb[i].TrimEnd() : "<missing>";
        if (!string.Equals(x, y, StringComparison.Ordinal)) return $"line {i + 1}: '{x}' vs '{y}'";
    }
    return "differ only in trailing whitespace";
}
