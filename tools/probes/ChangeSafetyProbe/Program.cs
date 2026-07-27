// DDL change safety (audit A-01) — live verification probe. See ChangeSafetyProbe.csproj for what and why.
//
//   $env:ET_LAB_PWD = "<sysdba password>"
//   dotnet run --project tools/probes/ChangeSafetyProbe
//
// Requires the local FB5 DefaultInstance on localhost:3050. Creates and drops its OWN scratch database at an
// ASCII path (gotcha #149) — the lab database is never touched.

using System.Diagnostics;
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

var scratchPath = @"C:\Temp\embertern_changesafety.fdb";
Directory.CreateDirectory(@"C:\Temp");

Console.WriteLine("DDL change safety (A-01) — live verification");
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
FbConnection.CreateDatabase(csb.ToString(), overwrite: true);
await using (var raw = new FbConnection(csb.ToString()))
{
    await raw.OpenAsync();
    await ExecAsync(raw, "CREATE TABLE T_MAIN (ID INTEGER NOT NULL PRIMARY KEY, NAME VARCHAR(50))");
    await ExecAsync(raw, "CREATE OR ALTER PROCEDURE SP_TARGET (P_IN INTEGER) RETURNS (R_OUT INTEGER) AS BEGIN R_OUT = P_IN * 2; SUSPEND; END");
    await ExecAsync(raw, "CREATE OR ALTER VIEW V_TARGET AS SELECT ID, NAME FROM T_MAIN");
}
Console.WriteLine("Scratch schema built (T_MAIN, SP_TARGET, V_TARGET).");
Console.WriteLine();

var profile = new ConnectionProfile
{
    Name = "changesafety",
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
var metadataReader = new FirebirdMetadataReader(service, lane);

Console.WriteLine($"Connected. Server: {service.RequireOpenConnection().ServerVersion}");
Console.WriteLine();

var procedure = new MetadataObject("SP_TARGET", MetadataObjectKind.Procedure);
var view = new MetadataObject("V_TARGET", MetadataObjectKind.View);

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
// CLAIM 1 — the reconstruction is DETERMINISTIC.
//
// The whole gate rests on this. If two reads of an unchanged object differ by even one byte, every Compile
// reports a false conflict and the gate is worse than the hazard.
// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Console.WriteLine("── CLAIM 1: reading an unchanged object twice yields the same fingerprint ───────────────");

var procFirst = await ddlReader.FetchProcedureSourceAsync(procedure);
var procSecond = await ddlReader.FetchProcedureSourceAsync(procedure);
var fpProcFirst = ObjectChangeSafety.Fingerprint(procFirst);
var fpProcSecond = ObjectChangeSafety.Fingerprint(procSecond);

Check("procedure: two reads are byte-identical", string.Equals(procFirst, procSecond, StringComparison.Ordinal));
Check("procedure: two reads fingerprint the same", fpProcFirst == fpProcSecond, fpProcFirst?[..12] ?? "null");
Check("procedure: the gate ALLOWS an unchanged object",
    ObjectChangeSafety.EvaluateOverwrite(fpProcFirst, procSecond) == ObjectChangeVerdict.Safe);

var viewFirst = await ddlReader.FetchViewSourceAsync(view);
var viewSecond = await ddlReader.FetchViewSourceAsync(view);
Check("view: two reads fingerprint the same",
    ObjectChangeSafety.Fingerprint(viewFirst) == ObjectChangeSafety.Fingerprint(viewSecond));

// Stability across many reads — a per-read nondeterminism (ordering, trailing whitespace) would show up as
// an occasional mismatch rather than a consistent one.
var stable = true;
for (var i = 0; i < 20; i++)
{
    var again = await ddlReader.FetchProcedureSourceAsync(procedure);
    if (!string.Equals(again, procFirst, StringComparison.Ordinal)) stable = false;
}
Check("procedure: stable over 20 consecutive reads", stable);
Console.WriteLine();

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
// CLAIM 2 — a real change IS detected, for the BODY and for the SIGNATURE.
//
// The fingerprint is taken over the reconstructed CREATE OR ALTER, not over the stored body blob, precisely
// so a signature-only change counts. That is the half worth proving: the body half is obvious.
// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Console.WriteLine("── CLAIM 2: a change by another session is detected ─────────────────────────────────────");

// "Another session" is literal: a separate attachment, exactly the audit's scenario.
await using (var other = new FbConnection(csb.ToString()))
{
    await other.OpenAsync();
    await ExecAsync(other, "CREATE OR ALTER PROCEDURE SP_TARGET (P_IN INTEGER) RETURNS (R_OUT INTEGER) AS BEGIN R_OUT = P_IN * 3; SUSPEND; END");
}

var afterBodyChange = await ddlReader.FetchProcedureSourceAsync(procedure);
Check("BODY change → fingerprint differs",
    ObjectChangeSafety.Fingerprint(afterBodyChange) != fpProcFirst);
Check("BODY change → the gate REFUSES",
    ObjectChangeSafety.EvaluateOverwrite(fpProcFirst, afterBodyChange) == ObjectChangeVerdict.ChangedInDatabase);

var fpAfterBody = ObjectChangeSafety.Fingerprint(afterBodyChange);
await using (var other = new FbConnection(csb.ToString()))
{
    await other.OpenAsync();
    // SIGNATURE only: the body text is character-for-character what it already was; only the parameter list
    // changes. A fingerprint over RDB$PROCEDURE_SOURCE alone would miss this entirely.
    await ExecAsync(other, "CREATE OR ALTER PROCEDURE SP_TARGET (P_IN BIGINT) RETURNS (R_OUT INTEGER) AS BEGIN R_OUT = P_IN * 3; SUSPEND; END");
}

var afterSignatureChange = await ddlReader.FetchProcedureSourceAsync(procedure);
Check("SIGNATURE-only change → fingerprint differs",
    ObjectChangeSafety.Fingerprint(afterSignatureChange) != fpAfterBody);
Check("SIGNATURE-only change → the gate REFUSES",
    ObjectChangeSafety.EvaluateOverwrite(fpAfterBody, afterSignatureChange) == ObjectChangeVerdict.ChangedInDatabase);

// A DROP is not a distinct verdict, by design — the reconstruction synthesizes a stub for a missing routine,
// so it simply reads as a change. This asserts that it still REFUSES, which is the behaviour that matters.
var fpBeforeDrop = ObjectChangeSafety.Fingerprint(afterSignatureChange);
await using (var other = new FbConnection(csb.ToString()))
{
    await other.OpenAsync();
    await ExecAsync(other, "DROP PROCEDURE SP_TARGET");
}
var afterDrop = await ddlReader.FetchProcedureSourceAsync(procedure);
Check("DROP by another session → the gate REFUSES",
    ObjectChangeSafety.EvaluateOverwrite(fpBeforeDrop, afterDrop) == ObjectChangeVerdict.ChangedInDatabase);
Console.WriteLine();

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
// CLAIM 3 — existence cannot be read off the reconstruction, which is why ExistsAsync exists.
// ════════════════════════════════════════════════════════════════════════════════════════════════════════
Console.WriteLine("── CLAIM 3: the New-object existence probe ──────────────────────────────────────────────");

// SP_TARGET is dropped now. The trap: the reconstruction returns a plausible, NON-EMPTY stub anyway.
Check("the reconstruction does NOT report a missing routine as empty",
    !string.IsNullOrWhiteSpace(afterDrop),
    $"returned {afterDrop.Length} chars for a dropped procedure");
Check("…so a fingerprint-based existence test would be WRONG",
    ObjectChangeSafety.Fingerprint(afterDrop) is not null);

Check("ExistsAsync: dropped procedure → false",
    !await metadataReader.ExistsAsync(MetadataObjectKind.Procedure, "SP_TARGET"));
Check("ExistsAsync: existing view → true",
    await metadataReader.ExistsAsync(MetadataObjectKind.View, "V_TARGET"));
Check("ExistsAsync: never-created name → false",
    !await metadataReader.ExistsAsync(MetadataObjectKind.Procedure, "SP_DOES_NOT_EXIST"));
Check("ExistsAsync: case-insensitive (Firebird folds unquoted identifiers)",
    await metadataReader.ExistsAsync(MetadataObjectKind.View, "v_target"));
Check("ExistsAsync: existing table → true",
    await metadataReader.ExistsAsync(MetadataObjectKind.Table, "T_MAIN"));

Check("EvaluateCreate REFUSES a taken name",
    ObjectChangeSafety.EvaluateCreate(
        await metadataReader.ExistsAsync(MetadataObjectKind.View, "V_TARGET")) == ObjectChangeVerdict.AlreadyExists);
Check("EvaluateCreate ALLOWS a free name",
    ObjectChangeSafety.EvaluateCreate(
        await metadataReader.ExistsAsync(MetadataObjectKind.Procedure, "SP_FREE")) == ObjectChangeVerdict.Safe);
Console.WriteLine();

// ── Cost, so the gate's price is a number rather than an opinion ─────────────────────────────────────────
Console.WriteLine("── COST of one check ───────────────────────────────────────────────────────────────────");
await ddlReader.FetchViewSourceAsync(view); // warm
var sw = Stopwatch.StartNew();
for (var i = 0; i < 10; i++) await ddlReader.FetchViewSourceAsync(view);
sw.Stop();
Console.WriteLine($"  overwrite check (re-read a view definition): {sw.Elapsed.TotalMilliseconds / 10:F1} ms");

var sw2 = Stopwatch.StartNew();
for (var i = 0; i < 10; i++) await metadataReader.ExistsAsync(MetadataObjectKind.Procedure, "SP_FREE");
sw2.Stop();
Console.WriteLine($"  create check (name list for this schema):    {sw2.Elapsed.TotalMilliseconds / 10:F1} ms");
Console.WriteLine();

await service.DisconnectAsync();
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

static async Task ExecAsync(FbConnection connection, string sql)
{
    // One statement, one auto-committed transaction — DDL must be committed before the next statement can
    // use the object (gotcha #213).
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    await cmd.ExecuteNonQueryAsync();
}
