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
// Stage X / D8 + D9 — step-into fidelity. Drives the REAL FirebirdDebugExecutor with Step Into and compares
// the SIMULATED output (the emitted SUSPEND row / the callee frame roster) to REAL execution of the same
// routines. D8: a 3-level STORED chain (SP_DBG_ROOT → SP_DBG_MID → SP_DBG_LEAF). D9 seam a part 2: a LOCAL
// sub-procedure (SP_DBG_LOCAL → ADD_TAX) — step into a local DECLARE PROCEDURE as a real frame, with a local
// DECLARE FUNCTION exercised server-side. D9 seam b Part 1: a local procedure that reads+writes an OUTER
// variable (SP_DBG_CLOSURE → BUMP) — closure capture over the declaring frame (FB5), stepped INTO. D9 seam b
// Part 2: the transitive read/write-set fixpoint — a local FUNCTION / PROCEDURE that reads+writes an outer
// variable NOT named at the call site (SP_DBG_CLOSURE_FN / SP_DBG_CLOSURE_OVER), stepped OVER. The authority is
// the engine, not us (Developer Contract #12: fidelity is proven against real execution).
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

    // Real execution returning one row as a column→value map (for the multi-column return-type comparison).
    async Task<Dictionary<string, object?>> RealRowAsync(string sql)
    {
        await using var cn = new FbConnection(csb.ToString());
        await cn.OpenAsync();
        await using var cmd = new FbCommand(sql, cn);
        await using var r = await cmd.ExecuteReaderAsync();
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (await r.ReadAsync())
        {
            for (int i = 0; i < r.FieldCount; i++)
            {
                row[r.GetName(i)] = await r.IsDBNullAsync(i) ? null : r.GetValue(i);
            }
        }
        return row;
    }

    // Simulate a standalone routine end-to-end and return (emitted rows, max depth, frame names). `step`
    // selects the movement command driven each pause — Into descends into resolvable local/stored calls;
    // Over runs a call in place (exercising the step-over harness + the D9 seam b Part 2 read/write fixpoint).
    async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int MaxDepth, List<string> Frames)>
        SimulateAsync(string routine, Dictionary<string, object?> rootValues, StepKind step = StepKind.Into)
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
                dbg.Step(step);
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

    // Simulate a TRIGGER body (D10) — the triggering DML is NOT performed (spec §8.1); the user supplies
    // NEW/OLD and we interpret the body. Fetches the full CREATE source, builds the trigger context (columns via
    // ContextSubstitution), seeds the NEW/OLD synthetics, steps to completion/fault, and reports the outcome, the
    // FINAL NEW values (read from the retained root frame — it keeps its values after the run) and an optional
    // inspection of the debug transaction (for a body that performs DML, captured before the §4.4 rollback).
    async Task<(DebugState State, string? Error,
                Dictionary<(TriggerRecord Rec, string Col), object?> Final, string? Inspected, List<string> Frames)>
        SimulateTriggerAsync(
            string triggerName, string table, TriggerEvent evt, TriggerTiming timing,
            Dictionary<(TriggerRecord Rec, string Col), object?> context,
            StepKind step = StepKind.Into,
            Func<DebugSessionConnection, Task<string?>>? inspect = null)
    {
        string source = await reader.FetchTriggerSourceAsync(new MetadataObject(triggerName, MetadataObjectKind.Trigger));
        var model = SemanticModel.Build(SqlParser.Parse(source).Root);
        var body = model.Syntax.Statements.OfType<DdlStatement>().First(d => d.Body is not null).Body!;

        var columns = ContextSubstitution.BuildColumns(model, new TextSpan(body.Start, body.Length));
        var trigger = new TriggerContext(table, evt, timing, columns);

        var rootValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in columns)
        {
            if (context.TryGetValue((c.Record, c.Column), out var v)) rootValues[c.Synthetic] = v;
        }

        var session = await service.CreateDebugSessionAsync(DebugIsolation.ReadCommitted);
        try
        {
            var executor = await FirebirdDebugExecutor.CreateAsync(session, triggerName, source, body, model, fallback, trigger);
            var dbg = new DebugSession(body, executor, triggerName, rootValues, source, model);
            dbg.Start();

            Frame? root = dbg.CurrentFrame; // retained — the frame keeps its values after it is popped
            var frames = new List<string>();
            int guard = 0;
            while (dbg.State == DebugState.Paused)
            {
                if (dbg.CurrentFrame is { } f && !frames.Contains(f.RoutineName)) frames.Add(f.RoutineName);
                dbg.Step(step);
                if (++guard > 5000) throw new Exception("runaway stepping");
            }

            string? inspected = inspect is not null ? await inspect(session) : null;

            var final = new Dictionary<(TriggerRecord, string), object?>();
            if (root is not null)
            {
                foreach (var c in columns)
                {
                    root.TryResolveValue(c.Synthetic, out var v);
                    final[(c.Record, c.Column)] = v;
                }
            }

            string? error = dbg.State == DebugState.Faulted
                ? (dbg.CurrentError?.ExceptionName ?? dbg.CurrentError?.Message) : null;
            return (dbg.State, error, final, inspected, frames);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    // Runs DML in a throwaway transaction and ROLLS BACK — the independent "real" reference for trigger effects
    // (the sim never persists). Returns the scalar from `query`, run in the same tx after `setup`.
    async Task<object?> RealInTxAsync(string[] setup, string query)
    {
        await using var cn = new FbConnection(csb.ToString());
        await cn.OpenAsync();
        var tx = cn.BeginTransaction();
        try
        {
            foreach (var s in setup)
            {
                await using var c = new FbCommand(s, cn, tx);
                await c.ExecuteNonQueryAsync();
            }
            await using var q = new FbCommand(query, cn, tx);
            return await q.ExecuteScalarAsync();
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    // Runs DML expected to raise, in a throwaway tx (rolled back); returns the exception message, or null if it
    // unexpectedly succeeded.
    async Task<string?> RealRaisesAsync(string sql)
    {
        await using var cn = new FbConnection(csb.ToString());
        await cn.OpenAsync();
        var tx = cn.BeginTransaction();
        try
        {
            await using var c = new FbCommand(sql, cn, tx);
            await c.ExecuteNonQueryAsync();
            return null;
        }
        catch (FbException ex) { return ex.Message; }
        finally { try { await tx.RollbackAsync(); } catch { /* best-effort */ } }
    }

    static int? AsInt(object? v) => v is null or DBNull ? null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    static Dictionary<string, object?> Root(int p) => new(StringComparer.OrdinalIgnoreCase) { ["P"] = p };
    // A type-agnostic display of a value for comparison — normalises across the driver's native types (int,
    // long, decimal, bool, string, null) via the invariant culture, so a per-column sim-vs-real check is exact.
    static string Show(object? v) => v is null or DBNull ? "<null>" : Convert.ToString(v, CultureInfo.InvariantCulture)!.Trim();

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

    // ── 4. Local sub-procedure + sub-function step-into (D9 seam a part 2 + seam c) ───────────
    Head("4. SP_DBG_LOCAL(5) — step INTO local FUNCTION TRIPLE (seam c) then local PROCEDURE ADD_TAX");
    var localRoot = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["BASE"] = 5 };
    var loc = await SimulateAsync("SP_DBG_LOCAL", localRoot);
    int? realLocal = AsInt(await RealScalarAsync("SELECT TOTAL FROM SP_DBG_LOCAL(5)"));
    int? simLocal = loc.Rows.Count > 0 ? AsInt(loc.Rows[0]["TOTAL"]) : null;

    if (loc.MaxDepth == 2) Pass("local depth == 2 (stepped into TRIPLE, then ADD_TAX)"); else Fail("local depth", $"{loc.MaxDepth}");
    // Seam c change: TRIPLE (a local FUNCTION) is now stepped INTO (ACC = TRIPLE(BASE)), not run server-side.
    if (loc.Frames.SequenceEqual(new[] { "SP_DBG_LOCAL", "TRIPLE", "ADD_TAX" }))
        Pass("local frame chain", string.Join(" → ", loc.Frames));
    else Fail("local frame chain", string.Join(" → ", loc.Frames));
    if (simLocal is not null && simLocal == realLocal)
        Pass("SIMULATED TOTAL == REAL", $"sim {simLocal} == real {realLocal}");
    else
        Fail("simulated vs real TOTAL", $"sim {simLocal} vs real {realLocal}");
    Console.WriteLine($"      (step into TRIPLE(5)=15, then ADD_TAX(15): BONUS=100 → WITH_TAX=115 → TOTAL, expected 115)");

    // ── 5. Closure capture — step into a local proc that reads+writes an outer var (D9 seam b) ──
    Head("5. SP_DBG_CLOSURE(5) — step INTO local BUMP twice; it reads+writes the OUTER var ACC (FB5 closure)");
    var cloRoot = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["SEED"] = 5 };
    var clo = await SimulateAsync("SP_DBG_CLOSURE", cloRoot);
    int? realClo = AsInt(await RealScalarAsync("SELECT TOTAL FROM SP_DBG_CLOSURE(5)"));
    int? simClo = clo.Rows.Count > 0 ? AsInt(clo.Rows[0]["TOTAL"]) : null;

    if (clo.MaxDepth == 2) Pass("closure depth == 2 (stepped into BUMP)"); else Fail("closure depth", $"{clo.MaxDepth}");
    if (clo.Frames.SequenceEqual(new[] { "SP_DBG_CLOSURE", "BUMP" }))
        Pass("closure frame chain", string.Join(" → ", clo.Frames));
    else Fail("closure frame chain", string.Join(" → ", clo.Frames));
    if (simClo is not null && simClo == realClo)
        Pass("SIMULATED TOTAL == REAL", $"sim {simClo} == real {realClo}");
    else
        Fail("simulated vs real TOTAL", $"sim {simClo} vs real {realClo}");
    Console.WriteLine($"      (BUMP captures outer ACC by reference: 5 → 15 → 25 → TOTAL, expected 25 — the closure write reaches the parent frame)");

    // ── 6. Transitive fixpoint — local FUNCTION with a HIDDEN capture, step OVER (D9 seam b Part 2) ──
    // Step OVER is explicit now: since seam c, a lone-call assignment (TOTAL = BUMP_HIDDEN(10)) is stepped
    // INTO under Step Into; this case still exercises the step-OVER fixpoint (the whole leaf runs server-side,
    // the fixpoint injecting the HIDDEN capture the call never names).
    Head("6. SP_DBG_CLOSURE_FN(5) — Step OVER a local FUNCTION that reads+writes outer HIDDEN (not named at the call)");
    var fnRoot = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["SEED"] = 5 };
    var fn = await SimulateAsync("SP_DBG_CLOSURE_FN", fnRoot, StepKind.Over);
    int? realFn = AsInt(await RealScalarAsync("SELECT TOTAL FROM SP_DBG_CLOSURE_FN(5)"));
    int? simFn = fn.Rows.Count > 0 ? AsInt(fn.Rows[0]["TOTAL"]) : null;
    if (fn.MaxDepth == 1) Pass("fn depth == 1 (function runs server-side, not stepped into)"); else Fail("fn depth", $"{fn.MaxDepth}");
    if (simFn is not null && simFn == realFn)
        Pass("SIMULATED TOTAL == REAL", $"sim {simFn} == real {realFn}");
    else
        Fail("simulated vs real TOTAL", $"sim {simFn} vs real {realFn}");
    Console.WriteLine($"      (fixpoint injects+returns the HIDDEN capture the call never names: 5 → BUMP_HIDDEN(10)=15 → TOTAL, expected 15)");

    // ── 7. Transitive fixpoint — local PROCEDURE with a HIDDEN capture, explicit Step OVER ──────────
    Head("7. SP_DBG_CLOSURE_OVER(5) — Step OVER a local PROCEDURE that reads+writes outer HIDDEN");
    var ovRoot = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["SEED"] = 5 };
    var ov = await SimulateAsync("SP_DBG_CLOSURE_OVER", ovRoot, StepKind.Over); // Step OVER the call → harness
    int? realOv = AsInt(await RealScalarAsync("SELECT TOTAL FROM SP_DBG_CLOSURE_OVER(5)"));
    int? simOv = ov.Rows.Count > 0 ? AsInt(ov.Rows[0]["TOTAL"]) : null;
    if (ov.MaxDepth == 1) Pass("over depth == 1 (call stepped over, not into)"); else Fail("over depth", $"{ov.MaxDepth}");
    if (simOv is not null && simOv == realOv)
        Pass("SIMULATED TOTAL == REAL", $"sim {simOv} == real {realOv}");
    else
        Fail("simulated vs real TOTAL", $"sim {simOv} vs real {realOv}");
    Console.WriteLine($"      (fixpoint injects+returns HIDDEN across the EXECUTE PROCEDURE the call never names: 5 → ACCUMULATE(10)=15 → TOTAL, expected 15)");

    // ── 8. Local FUNCTION step-into — the four value-consuming positions (D9 seam c, §6.4) ──────────
    Head("8. SP_DBG_FN_POS(5) — step INTO a local FUNCTION in all four positions (=, RETURN, IF, WHILE)");
    var pos = await SimulateAsync("SP_DBG_FN_POS", Root(5));
    int? realPos = AsInt(await RealScalarAsync("SELECT RESULT FROM SP_DBG_FN_POS(5)"));
    int? simPos = pos.Rows.Count > 0 ? AsInt(pos.Rows[0]["RESULT"]) : null;
    if (pos.MaxDepth == 3) Pass("pos depth == 3 (SP_DBG_FN_POS → WRAP → INC, via the RETURN operand)");
    else Fail("pos depth", $"{pos.MaxDepth}");
    if (pos.Frames.Contains("INC") && pos.Frames.Contains("POSITIVE") && pos.Frames.Contains("WRAP"))
        Pass("pos stepped into every position's function", string.Join(" → ", pos.Frames));
    else Fail("pos frames", string.Join(" → ", pos.Frames));
    if (simPos is not null && simPos == realPos) Pass("SIMULATED RESULT == REAL", $"sim {simPos} == real {realPos}");
    else Fail("simulated vs real RESULT", $"sim {simPos} vs real {realPos}");
    Console.WriteLine("      (= : INC ; IF/WHILE : POSITIVE ; RETURN operand : WRAP→INC — RESULT expected 10)");

    // ── 9. Local FUNCTION return types — the Expression Harness vs the server, across types ──────────
    Head("9. SP_DBG_FN_TYPES — step INTO a local FUNCTION per return type (INTEGER/BIGINT/NUMERIC/VARCHAR/BOOLEAN/NULL)");
    var types = await SimulateAsync("SP_DBG_FN_TYPES", new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
    var realTypes = await RealRowAsync("SELECT R_INT, R_BIG, R_NUM, R_TXT, R_BOOL, R_NUL FROM SP_DBG_FN_TYPES");
    var simTypes = types.Rows.Count > 0 ? types.Rows[0] : null;
    if (types.MaxDepth == 2) Pass("types depth == 2 (each function stepped into)"); else Fail("types depth", $"{types.MaxDepth}");
    if (simTypes is null)
    {
        Fail("types row", "no SUSPEND row emitted");
    }
    else
    {
        foreach (var col in new[] { "R_INT", "R_BIG", "R_NUM", "R_TXT", "R_BOOL", "R_NUL" })
        {
            string sim = Show(simTypes.TryGetValue(col, out var sv) ? sv : null);
            string real = Show(realTypes.TryGetValue(col, out var rv) ? rv : null);
            if (sim == real) Pass($"{col}: sim == real", real);
            else Fail($"{col}: sim vs real", $"sim {sim} vs real {real}");
        }
    }
    Console.WriteLine("      (each RETURN operand computed by the Expression Harness typed as the function's RETURNS base type)");

    // ── 10. Shadowing — a local function shadows a same-named stored function (D9 seam c) ────────────
    Head("10. SP_DBG_FN_SHADOW(5) — a LOCAL function shadows the stored FN_ADD_TAX; the LOCAL must be chosen");
    var shadow = await SimulateAsync("SP_DBG_FN_SHADOW", Root(5));
    int? realShadow = AsInt(await RealScalarAsync("SELECT RESULT FROM SP_DBG_FN_SHADOW(5)"));
    int? simShadow = shadow.Rows.Count > 0 ? AsInt(shadow.Rows[0]["RESULT"]) : null;
    if (shadow.MaxDepth == 2) Pass("shadow depth == 2 (stepped INTO the LOCAL FN_ADD_TAX, not the stored global)");
    else Fail("shadow depth", $"{shadow.MaxDepth} (1 ⇒ resolved to the stored global — wrong definition)");
    if (shadow.Frames.Contains("FN_ADD_TAX")) Pass("shadow frame", string.Join(" → ", shadow.Frames));
    else Fail("shadow frame", string.Join(" → ", shadow.Frames));
    if (simShadow is not null && simShadow == realShadow) Pass("SIMULATED RESULT == REAL", $"sim {simShadow} == real {realShadow}");
    else Fail("simulated vs real RESULT", $"sim {simShadow} vs real {realShadow}");
    Console.WriteLine("      (the local FN_ADD_TAX(N) returns N+5000, unlike the 2-arg stored one — RESULT expected 5005)");

    // ── 11. Closure — a local FUNCTION reads an outer variable, stepped into (D9 seam c) ─────────────
    Head("11. SP_DBG_FN_CLOSURE(5) — step INTO a local FUNCTION that CLOSES OVER the outer variable BASE");
    var fnClo = await SimulateAsync("SP_DBG_FN_CLOSURE", Root(5));
    int? realFnClo = AsInt(await RealScalarAsync("SELECT RESULT FROM SP_DBG_FN_CLOSURE(5)"));
    int? simFnClo = fnClo.Rows.Count > 0 ? AsInt(fnClo.Rows[0]["RESULT"]) : null;
    if (fnClo.MaxDepth == 2) Pass("fn-closure depth == 2 (stepped into ADD_BASE)"); else Fail("fn-closure depth", $"{fnClo.MaxDepth}");
    if (fnClo.Frames.Contains("ADD_BASE")) Pass("fn-closure frame", string.Join(" → ", fnClo.Frames));
    else Fail("fn-closure frame", string.Join(" → ", fnClo.Frames));
    if (simFnClo is not null && simFnClo == realFnClo) Pass("SIMULATED RESULT == REAL", $"sim {simFnClo} == real {realFnClo}");
    else Fail("simulated vs real RESULT", $"sim {simFnClo} vs real {realFnClo}");
    Console.WriteLine("      (ADD_BASE reads outer BASE=100 by closure: 5 + 100 = 105)");

    // ══ Stage X / D10 — TRIGGERS (spec §8.1). The triggering DML is not performed; the user supplies NEW/OLD ══
    // and we interpret the body. Fidelity = the body's EFFECTS vs a real DML that fires the trigger (rolled back).

    // ── 12. BEFORE UPDATE exception (TR_ORDERS_BU) — NEW context, sim vs real ─
    Head("12. TR_ORDERS_BU — BEFORE UPDATE; NEW.TOTAL_AMOUNT < 0 raises E_NEGATIVE_AMOUNT (sim vs real)");
    var buNeg = await SimulateTriggerAsync("TR_ORDERS_BU", "ORDERS", TriggerEvent.Update, TriggerTiming.Before,
        new() { [(TriggerRecord.New, "TOTAL_AMOUNT")] = -5m });
    string? realBu = await RealRaisesAsync(
        "UPDATE ORDERS SET TOTAL_AMOUNT = -5 WHERE ORDER_ID = (SELECT MIN(ORDER_ID) FROM ORDERS)");
    if (buNeg.State == DebugState.Faulted && (buNeg.Error?.Contains("E_NEGATIVE_AMOUNT") ?? false))
        Pass("BU sim faults E_NEGATIVE_AMOUNT", buNeg.Error!);
    else Fail("BU sim fault", $"{buNeg.State} / {buNeg.Error}");
    if (realBu is not null && realBu.Contains("E_NEGATIVE_AMOUNT")) Pass("BU real faults too (sim == real: same exception)");
    else Fail("BU real fault", realBu ?? "no exception");
    var buOk = await SimulateTriggerAsync("TR_ORDERS_BU", "ORDERS", TriggerEvent.Update, TriggerTiming.Before,
        new() { [(TriggerRecord.New, "TOTAL_AMOUNT")] = 100m });
    if (buOk.State == DebugState.Completed) Pass("BU with a non-negative amount completes (no fault)");
    else Fail("BU non-negative", $"{buOk.State} / {buOk.Error}");

    // ── 13. AFTER UPDATE side-effect (TR_ORDERS_AU) — OLD+NEW read-only, sim vs real DML ──
    Head("13. TR_ORDERS_AU — AFTER UPDATE; a STATUS change writes an AUDIT_LOG row (sim vs real DETAILS)");
    const string auDetails = "Status changed from ACT to DONE";
    var au = await SimulateTriggerAsync("TR_ORDERS_AU", "ORDERS", TriggerEvent.Update, TriggerTiming.After,
        new()
        {
            [(TriggerRecord.Old, "STATUS")] = "ACT",
            [(TriggerRecord.New, "STATUS")] = "DONE",
            [(TriggerRecord.New, "ORDER_ID")] = 1,
        },
        inspect: async conn =>
        {
            await using var cmd = conn.Connection.CreateCommand();
            cmd.CommandText = "SELECT DETAILS FROM AUDIT_LOG WHERE ACTION = 'STATUS_CHANGE' ORDER BY LOG_ID DESC ROWS 1";
            cmd.Transaction = conn.Transaction;
            var r = await cmd.ExecuteScalarAsync();
            return r is null or DBNull ? null : Convert.ToString(r, CultureInfo.InvariantCulture)?.Trim();
        });
    var realAu = await RealInTxAsync(
        new[]
        {
            "UPDATE ORDERS SET STATUS = 'ACT'  WHERE ORDER_ID = (SELECT MIN(ORDER_ID) FROM ORDERS)",
            "UPDATE ORDERS SET STATUS = 'DONE' WHERE ORDER_ID = (SELECT MIN(ORDER_ID) FROM ORDERS)",
        },
        "SELECT DETAILS FROM AUDIT_LOG WHERE ACTION = 'STATUS_CHANGE' ORDER BY LOG_ID DESC ROWS 1");
    string? realAuDetails = realAu is null or DBNull ? null : Convert.ToString(realAu, CultureInfo.InvariantCulture)?.Trim();
    if (au.Inspected == auDetails) Pass("AU sim inserted the audit row into the debug tx", au.Inspected!);
    else Fail("AU sim audit", $"{au.State}/{au.Error ?? "ok"} inspected={au.Inspected ?? "<none>"}");
    if (realAuDetails == auDetails) Pass("AU real UPDATE produced the same audit row (sim == real)", realAuDetails!);
    else Fail("AU real audit", $"sim '{auDetails}' vs real '{realAuDetails}'");

    // ── 14. BEFORE DELETE, OLD-only (TR_TRIG_BD) — NEW unavailable, sim vs real ──
    Head("14. TR_TRIG_BD — BEFORE DELETE (OLD-only); OLD.STATUS='LOCKED' raises E_ORDER_LOCKED (sim vs real)");
    var bdLocked = await SimulateTriggerAsync("TR_TRIG_BD", "TRIG_LAB", TriggerEvent.Delete, TriggerTiming.Before,
        new() { [(TriggerRecord.Old, "STATUS")] = "LOCKED" });
    if (bdLocked.State == DebugState.Faulted && (bdLocked.Error?.Contains("E_ORDER_LOCKED") ?? false))
        Pass("BD sim faults E_ORDER_LOCKED on a locked row", bdLocked.Error!);
    else Fail("BD sim fault", $"{bdLocked.State} / {bdLocked.Error}");
    string? realBd = await RealRaisesAsync(
        "EXECUTE BLOCK AS BEGIN " +
        "  INSERT INTO TRIG_LAB (ID, STATUS) VALUES (9001, 'LOCKED'); " +
        "  DELETE FROM TRIG_LAB WHERE ID = 9001; " +
        "END");
    if (realBd is not null && realBd.Contains("E_ORDER_LOCKED")) Pass("BD real DELETE faults too (sim == real)");
    else Fail("BD real fault", realBd ?? "no exception");
    var bdOk = await SimulateTriggerAsync("TR_TRIG_BD", "TRIG_LAB", TriggerEvent.Delete, TriggerTiming.Before,
        new() { [(TriggerRecord.Old, "STATUS")] = "ACTIVE" });
    if (bdOk.State == DebugState.Completed) Pass("BD on a non-locked row completes (no fault)");
    else Fail("BD non-locked", $"{bdOk.State} / {bdOk.Error}");

    // ── 15. BEFORE INSERT, multi-action predicate (TR_TRIG_BIU) — NEW writable, sim vs real ──
    Head("15. TR_TRIG_BIU — BEFORE INSERT (multi-action); INSERTING ⇒ NEW.NOTE='INSERTED' (sim vs real)");
    var biuIns = await SimulateTriggerAsync("TR_TRIG_BIU", "TRIG_LAB", TriggerEvent.Insert, TriggerTiming.Before,
        new() { [(TriggerRecord.New, "NOTE")] = null });
    string simInsNote = Show(biuIns.Final.TryGetValue((TriggerRecord.New, "NOTE"), out var vi) ? vi : null);
    object? realIns = await RealInTxAsync(
        new[] { "INSERT INTO TRIG_LAB (ID, STATUS) VALUES (9002, 'NEW')" },
        "SELECT NOTE FROM TRIG_LAB WHERE ID = 9002");
    if (biuIns.State == DebugState.Completed && simInsNote == "INSERTED") Pass("BIU INSERTING ⇒ NEW.NOTE='INSERTED' (sim)", simInsNote);
    else Fail("BIU insert sim", $"{biuIns.State} / NOTE={simInsNote}");
    if (Show(realIns) == "INSERTED") Pass("BIU real INSERT persists NOTE='INSERTED' (sim == real)");
    else Fail("BIU insert real", Show(realIns));

    // ── 16. BEFORE UPDATE via the SAME multi-action trigger — the UPDATING predicate ──
    Head("16. TR_TRIG_BIU — BEFORE UPDATE (same trigger, other action); UPDATING ⇒ NEW.NOTE='UPDATED' (sim vs real)");
    var biuUpd = await SimulateTriggerAsync("TR_TRIG_BIU", "TRIG_LAB", TriggerEvent.Update, TriggerTiming.Before,
        new() { [(TriggerRecord.New, "NOTE")] = null });
    string simUpdNote = Show(biuUpd.Final.TryGetValue((TriggerRecord.New, "NOTE"), out var vu) ? vu : null);
    object? realUpd = await RealInTxAsync(
        new[]
        {
            "INSERT INTO TRIG_LAB (ID, STATUS) VALUES (9003, 'NEW')",
            "UPDATE TRIG_LAB SET STATUS = 'X' WHERE ID = 9003",
        },
        "SELECT NOTE FROM TRIG_LAB WHERE ID = 9003");
    if (biuUpd.State == DebugState.Completed && simUpdNote == "UPDATED") Pass("BIU UPDATING ⇒ NEW.NOTE='UPDATED' (sim)", simUpdNote);
    else Fail("BIU update sim", $"{biuUpd.State} / NOTE={simUpdNote}");
    if (Show(realUpd) == "UPDATED") Pass("BIU real UPDATE persists NOTE='UPDATED' (sim == real; multi-action, same trigger)");
    else Fail("BIU update real", Show(realUpd));
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
