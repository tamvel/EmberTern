using System.Globalization;
using System.Linq;
using System.Text;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;

// DEVELOPER VERIFICATION TOOL — NOT PART OF THE PRODUCT. See tools/probes/README.md.
//
// Stage X / D8 — nested stored-routine step-into fidelity. Drives the REAL FirebirdDebugExecutor through
// a 3-level chain (SP_DBG_ROOT → SP_DBG_MID → SP_DBG_LEAF) with Step Into, and compares the SIMULATED
// output (the emitted SUSPEND row / the callee frame roster) to REAL execution of the same routines. The
// authority is the engine, not us (Developer Contract #12: fidelity is proven against real execution).
//
//   $env:ET_LAB_PWD = "<local dev SYSDBA password>"
//   dotnet run --project tools\probes\DebuggerFidelityProbe

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

var pwd = Environment.GetEnvironmentVariable("ET_LAB_PWD");
if (string.IsNullOrWhiteSpace(pwd))
{
    Console.WriteLine("Set ET_LAB_PWD to the local dev SYSDBA password.");
    return 2;
}

// The repo lab DB — the managed driver reaches the non-ASCII repo path fine (gotcha #149).
string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
string labPath = Path.Combine(repo, "Lab", "EmberTern_Lab.fdb");
if (!File.Exists(labPath)) { Console.WriteLine($"Lab DB not found at {labPath}"); return 2; }

var profile = new ConnectionProfile
{
    Name = "lab", Host = "localhost", Port = 3050, DatabasePath = labPath,
    Username = "SYSDBA", Password = pwd, Charset = "WIN1250", Dialect = 3,
};

int failures = 0;
void Pass(string what, string detail = "") => Console.WriteLine($"  PASS  {what}{(detail.Length > 0 ? "  — " + detail : "")}");
void Fail(string what, string detail) { failures++; Console.WriteLine($"  FAIL  {what}  — {detail}"); }
void Head(string t) => Console.WriteLine($"\n=== {t} ===");

// A throwaway direct connection for REAL execution (same lab, same wire path).
var csb = new FbConnectionStringBuilder
{
    Database = labPath, DataSource = "localhost", Port = 3050, UserID = "SYSDBA",
    Password = pwd, Charset = "WIN1250", Dialect = 3, ServerType = FbServerType.Default, Pooling = false,
};

var service = new FirebirdConnectionService();
try
{
    await service.ConnectAsync(profile);
    Console.WriteLine($"Connected: {service.IsConnected}  DB: {labPath}");

    var reader = new FirebirdDdlReader(service);
    var fallback = CharsetCatalog.Resolve(profile.Charset);

    // Real execution of a selectable / executable procedure returning one scalar.
    async Task<object?> RealScalarAsync(string sql)
    {
        await using var cn = new FbConnection(csb.ToString());
        await cn.OpenAsync();
        await using var cmd = new FbCommand(sql, cn);
        return await cmd.ExecuteScalarAsync();
    }

    // Simulate a standalone routine end-to-end via Step Into and return (emitted rows, max depth, frame names).
    async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int MaxDepth, List<string> Frames)>
        SimulateAsync(string routine, Dictionary<string, object?> rootValues)
    {
        string source = await reader.FetchProcedureSourceAsync(new MetadataObject(routine, MetadataObjectKind.Procedure));
        var model = SemanticModel.Build(SqlParser.Parse(source).Root);
        var body = model.Syntax.Statements.OfType<DdlStatement>().First(d => d.Body is not null).Body!;

        var session = await service.CreateDebugSessionAsync(DebugIsolation.ReadCommitted);
        try
        {
            var executor = await FirebirdDebugExecutor.CreateAsync(session, routine, source, body, model, fallback);
            var dbg = new DebugSession(body, executor, routine, rootValues);
            dbg.Start();

            int maxDepth = 0;
            var frames = new List<string>();
            int guard = 0;
            while (dbg.State == DebugState.Paused)
            {
                if (dbg.Depth > maxDepth) maxDepth = dbg.Depth;
                if (dbg.CurrentFrame is { } f && !frames.Contains(f.RoutineName)) frames.Add(f.RoutineName);
                dbg.Step(StepKind.Into);
                if (++guard > 5000) throw new Exception("runaway stepping");
            }
            if (dbg.State == DebugState.Faulted)
                throw new Exception($"faulted: {dbg.CurrentError?.Message ?? dbg.CurrentError?.ExceptionName}");
            return (dbg.EmittedRows, maxDepth, frames);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    static int? AsInt(object? v) => v is null or DBNull ? null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    static Dictionary<string, object?> Root(int p) => new(StringComparer.OrdinalIgnoreCase) { ["P"] = p };

    // ── 1. Leaf (no descent) ────────────────────────────────────────────────
    Head("1. SP_DBG_LEAF(5) — single frame, RETURNS Q = P + 1");
    var leaf = await SimulateAsync("SP_DBG_LEAF", Root(5));
    int? realLeaf = AsInt(await RealScalarAsync("EXECUTE PROCEDURE SP_DBG_LEAF(5)"));
    // An executable (non-SUSPEND) proc emits no row; its output is proven through MID/ROOT (which read it via
    // RETURNING_VALUES). Here we only assert it does not descend (depth 1) and print the real value.
    if (leaf.MaxDepth == 1) Pass("leaf depth == 1 (no descent)"); else Fail("leaf depth", $"{leaf.MaxDepth}");
    Console.WriteLine($"      real EXECUTE PROCEDURE SP_DBG_LEAF(5) → Q = {realLeaf}");

    // ── 2. Mid (2 frames) — step into LEAF, RETURNING_VALUES write-back ──────
    Head("2. SP_DBG_MID(5) — step into SP_DBG_LEAF, RETURNING_VALUES, Q = T*2");
    var mid = await SimulateAsync("SP_DBG_MID", Root(5));
    int? realMid = AsInt(await RealScalarAsync("EXECUTE PROCEDURE SP_DBG_MID(5)"));
    if (mid.MaxDepth == 2) Pass("mid depth == 2 (stepped into LEAF)"); else Fail("mid depth", $"{mid.MaxDepth}");
    if (mid.Frames.SequenceEqual(new[] { "SP_DBG_MID", "SP_DBG_LEAF" }))
        Pass("mid frame chain", string.Join(" → ", mid.Frames));
    else Fail("mid frame chain", string.Join(" → ", mid.Frames));
    Console.WriteLine($"      real SP_DBG_MID(5) → Q = {realMid} (expected 12)");

    // ── 3. Root (3 frames A→B→C) — the DoD chain, output vs real ─────────────
    Head("3. SP_DBG_ROOT(5) — A→B→C (ROOT→MID→LEAF), SUSPEND RESULT = T+100");
    var root = await SimulateAsync("SP_DBG_ROOT", Root(5));
    int? realRoot = AsInt(await RealScalarAsync("SELECT RESULT FROM SP_DBG_ROOT(5)"));
    int? simRoot = root.Rows.Count > 0 ? AsInt(root.Rows[0]["RESULT"]) : null;

    if (root.MaxDepth == 3) Pass("root depth == 3 (A→B→C)"); else Fail("root depth", $"{root.MaxDepth}");
    if (root.Frames.SequenceEqual(new[] { "SP_DBG_ROOT", "SP_DBG_MID", "SP_DBG_LEAF" }))
        Pass("root frame chain", string.Join(" → ", root.Frames));
    else Fail("root frame chain", string.Join(" → ", root.Frames));

    if (simRoot is not null && simRoot == realRoot)
        Pass("SIMULATED RESULT == REAL", $"sim {simRoot} == real {realRoot}");
    else
        Fail("simulated vs real RESULT", $"sim {simRoot} vs real {realRoot}");
    Console.WriteLine($"      (arg seeding + RETURNING_VALUES across 3 levels: LEAF(5)=6, MID=12, ROOT=112)");
}
catch (Exception ex)
{
    Fail("probe", ex.Message);
    Console.WriteLine(ex);
}
finally
{
    await service.DisconnectAsync();
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
