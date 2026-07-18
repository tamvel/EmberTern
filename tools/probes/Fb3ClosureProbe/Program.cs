using System.Globalization;
using System.Text;
using FirebirdSql.Data.FirebirdClient;

// DEVELOPER VERIFICATION TOOL — NOT PART OF THE PRODUCT. See tools/probes/README.md.
//
// Stage X / D9 — spec §6.3 VERSION GATE. Does a PSQL sub-routine (DECLARE FUNCTION / DECLARE PROCEDURE
// inside EXECUTE BLOCK) capture an OUTER variable of the enclosing block — read it (Q2), see it mutated
// by reference (Q3), and write it back (Q4)? §6.1 measured YES on FB5.0 only; FB3 historically documented
// sub-routines as having NO outer access. If FB3 differs, the D9 closure harness must branch on version.
//
// Raw EXECUTE BLOCK against a throwaway scratch DB created on each instance — no tables, no metadata, and
// crucially NO EmberTern interpreter: this measures the ENGINE, per Developer Contract "verify, don't infer".
//
//   $env:ET_LAB_PWD = "<local dev SYSDBA password>"
//   dotnet run --project tools\probes\Fb3ClosureProbe

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

var pwd = Environment.GetEnvironmentVariable("ET_LAB_PWD");
if (string.IsNullOrWhiteSpace(pwd))
{
    Console.WriteLine("Set ET_LAB_PWD to the local dev SYSDBA password.");
    return 2;
}

int failures = 0;
void Fail(string what, string detail) { failures++; Console.WriteLine($"  FAIL  {what}  — {detail}"); }

// ── The three closure probes (map exactly to §6.1 Q2/Q3/Q4) ──────────────────────────────────────────
//
// Q2 — a sub-FUNCTION reads an outer variable (returns OUTER_V + 1 for OUTER_V = 5 ⇒ 6).
const string Q2 = @"
EXECUTE BLOCK RETURNS (RESULT INTEGER) AS
  DECLARE OUTER_V INTEGER;
  DECLARE FUNCTION F RETURNS INTEGER AS
  BEGIN
    RETURN OUTER_V + 1;
  END
BEGIN
  OUTER_V = 5;
  RESULT = F();
  SUSPEND;
END";

// Q3 — the sub-FUNCTION sees the outer variable MUTATED between calls (by reference): 5 then 99.
const string Q3 = @"
EXECUTE BLOCK RETURNS (R1 INTEGER, R2 INTEGER) AS
  DECLARE OUTER_V INTEGER;
  DECLARE FUNCTION F RETURNS INTEGER AS
  BEGIN
    RETURN OUTER_V;
  END
BEGIN
  OUTER_V = 5;  R1 = F();
  OUTER_V = 99; R2 = F();
  SUSPEND;
END";

// Q4 — a sub-PROCEDURE WRITES an outer variable (sets OUTER_V = 77; caller reads 77 back).
const string Q4 = @"
EXECUTE BLOCK RETURNS (RESULT INTEGER) AS
  DECLARE OUTER_V INTEGER;
  DECLARE PROCEDURE P AS
  BEGIN
    OUTER_V = 77;
  END
BEGIN
  OUTER_V = 0;
  EXECUTE PROCEDURE P;
  RESULT = OUTER_V;
  SUSPEND;
END";

// Run one EXECUTE BLOCK probe; return (compiled?, first-row values or the compile/runtime error text).
static async Task<(bool Ok, string Detail)> RunProbeAsync(FbConnection cn, string sql)
{
    try
    {
        await using var cmd = new FbCommand(sql, cn);
        await using var rdr = await cmd.ExecuteReaderAsync();
        var vals = new List<string>();
        if (await rdr.ReadAsync())
            for (int i = 0; i < rdr.FieldCount; i++)
                vals.Add($"{rdr.GetName(i)}={(rdr.IsDBNull(i) ? "<null>" : rdr.GetValue(i))}");
        return (true, vals.Count > 0 ? string.Join(", ", vals) : "(no row)");
    }
    catch (FbException ex)
    {
        // A "closures unsupported" engine reports the outer var as unknown at COMPILE time.
        return (false, ex.Message.Replace("\r", " ").Replace("\n", " ").Trim());
    }
}

// Probe one instance: create a scratch DB, run Q2/Q3/Q4, drop the DB. Returns the closure verdict.
async Task ProbeInstanceAsync(string label, int port)
{
    Console.WriteLine($"\n=== {label} @ localhost:{port} ===");
    string dbPath = Path.Combine(Path.GetTempPath(), $"et_closure_probe_{port}.fdb");

    var csb = new FbConnectionStringBuilder
    {
        Database = dbPath, DataSource = "localhost", Port = port, UserID = "SYSDBA",
        Password = pwd, Charset = "UTF8", Dialect = 3, ServerType = FbServerType.Default, Pooling = false,
    };

    try
    {
        try { FbConnection.DropDatabase(csb.ToString()); } catch { /* not there yet */ }
        FbConnection.CreateDatabase(csb.ToString(), pageSize: 8192, forcedWrites: false, overwrite: true);

        await using var cn = new FbConnection(csb.ToString());
        await cn.OpenAsync();

        // Report the actual server version so the log is unambiguous.
        Console.WriteLine($"  server: {cn.ServerVersion}");

        var q2 = await RunProbeAsync(cn, Q2);
        var q3 = await RunProbeAsync(cn, Q3);
        var q4 = await RunProbeAsync(cn, Q4);

        Console.WriteLine($"  Q2 (sub-fn READS outer):        {(q2.Ok ? "COMPILED" : "REJECTED")}  {q2.Detail}");
        Console.WriteLine($"  Q3 (sees outer MUTATED byref):  {(q3.Ok ? "COMPILED" : "REJECTED")}  {q3.Detail}");
        Console.WriteLine($"  Q4 (sub-proc WRITES outer):     {(q4.Ok ? "COMPILED" : "REJECTED")}  {q4.Detail}");

        bool closures = q2.Ok && q3.Ok && q4.Ok;
        Console.WriteLine($"  ⇒ VERDICT: sub-routines {(closures ? "ARE closures over the parent frame (read+write)" : "are NOT closures — CLOSED scope")}");

        // Sanity-check the expected values when closures are present (catches a silent wrong answer).
        if (closures)
        {
            if (q2.Detail != "RESULT=6") Fail($"{label} Q2 value", $"expected RESULT=6, got {q2.Detail}");
            if (q3.Detail != "R1=5, R2=99") Fail($"{label} Q3 value", $"expected R1=5, R2=99, got {q3.Detail}");
            if (q4.Detail != "RESULT=77") Fail($"{label} Q4 value", $"expected RESULT=77, got {q4.Detail}");
        }

        await cn.CloseAsync();
    }
    catch (Exception ex)
    {
        Fail($"{label} probe", ex.Message);
        Console.WriteLine(ex);
    }
    finally
    {
        try { FbConnection.ClearAllPools(); FbConnection.DropDatabase(csb.ToString()); } catch { /* best effort */ }
    }
}

await ProbeInstanceAsync("Firebird 3 (baseline for §6.3 gate)", 4050);
await ProbeInstanceAsync("Firebird 5 (§6.1 confirmation)", 3050);
Console.WriteLine("\nFirebird 4: NOT INSTALLED in this environment — recorded unverified (same posture as P2's FB2.5).");

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
