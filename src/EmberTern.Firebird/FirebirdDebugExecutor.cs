using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Firebird;

/// <summary>
/// The one server seam of the debugger (Stage X / D2 seam c, spec §3.2/§3.3) — <see cref="IDebugExecutor"/>
/// over a live Firebird session. It wires D1's pure interpreter (<see cref="DebugSession"/>) to seam (a)'s
/// <see cref="DebugSessionConnection"/> through seam (b)'s <see cref="HarnessBuilder"/>: every step, DML leaf
/// and condition becomes a generated anonymous <c>EXECUTE BLOCK</c> run in the debug transaction, and the
/// server computes <b>all</b> semantics (evaluation order, types, collations, <c>NULL</c>). The executor
/// never evaluates an expression, coerces a type, or decides a boolean itself — that is the whole design
/// (Developer Contract #3/#4).
/// <para>
/// The read/write set (<see cref="ReadWriteSetAnalyzer"/>, §3.5) narrows the injected payload, the §3.4 rules
/// live in <see cref="HarnessBuilder"/>, and the frame variable declarations (R3) + base types (R2) are
/// resolved once at construction by <see cref="FirebirdDebugMetadata"/>. Errors map to the interpreter's
/// <see cref="DebugError"/> through <see cref="DebugErrorMapper"/> (SQLSTATE/GDS from the driver, never parsed
/// from a message). Frame savepoints (§4.5) delegate to the session.
/// </para>
/// <para>
/// <b>Boundaries (§F — explained, not guessed):</b> stepping <em>into</em> a standalone <c>EXECUTE
/// PROCEDURE</c> resolves the callee for a real frame (<see cref="ResolveRoutine"/>, D8 — fetch/parse/seed);
/// a call it cannot faithfully descend into (a package/qualified name — D11, a local sub-routine — D9, or a
/// callee whose source/metadata cannot be read) runs on the server in place, which is <em>step-over</em> and
/// 100% faithful (§5.3). A <c>FOR SELECT</c> cursor (<see cref="OpenCursor"/>) is stepped through the Cursor
/// Bridge (D6, §7); a <c>FOR EXECUTE STATEMENT</c> dynamic cursor is refused with a clear message.
/// </para>
/// <para>
/// <b>Threading.</b> <see cref="IDebugExecutor"/> is synchronous (D1's frozen contract); the session is async.
/// This executor bridges by blocking (<see cref="Await{T}"/>). That is deadlock-safe because
/// <see cref="DebugSessionConnection"/> and this class use <c>ConfigureAwait(false)</c> throughout (no
/// synchronization-context capture), and stepping is driven off the UI thread (D4). Every wire operation
/// captures the session's single command lock once (gotchas #98/#120/#236 — interleaving is fine, concurrency
/// is not).
/// </para>
/// </summary>
public sealed class FirebirdDebugExecutor : IDebugExecutor
{
    private const string BooleanResultType = "BOOLEAN";

    // The result-column type for an arbitrary user expression (D5 / §9.5). An arbitrary expression has no
    // known type (unlike an IF/WHILE condition, which is BOOLEAN), so the server casts the evaluated value to
    // a wide UTF8 VARCHAR and we surface it as text — the honest general choice for a display surface
    // (typed, per-kind inspection of a declared variable is the Variables window, D7). A value that cannot
    // cast to VARCHAR (e.g. a binary BLOB) raises and is surfaced as the error, never silently guessed (§F).
    private const string EvaluationResultType = "VARCHAR(8191) CHARACTER SET UTF8";

    // A synthetic result-variable name for one evaluated call argument (D8 step-into). ET_-prefixed so it
    // cannot collide with a real ERP variable (same convention as HarnessBuilder's ET_P_/ET_O_).
    private const string ArgVarPrefix = "ET_ARG_";

    private readonly DebugSessionConnection _session;
    private readonly Encoding _fallback;

    // Per-routine context, keyed by the routine's body node (Stage X / D8): the interpreter runs a call stack
    // of frames, each an activation of a routine whose source / model / variable templates / outputs differ.
    // Every executor method reads the context for the frame it operates on (via the frame's Body — the stable
    // key). One entry per distinct routine body: recursion (the same body on two frames) shares one context
    // (same declarations + types; the per-frame VALUES live on the Frame). The root is registered at
    // construction; a stepped-into stored routine registers its own in ResolveRoutine (D8 seam b part 2).
    private readonly Dictionary<BlockStatement, RoutineContext> _contexts = new();

    private sealed record RoutineContext(
        string Source,
        SemanticModel Model,
        IReadOnlyList<HarnessVariable> VariableTemplates,
        HashSet<string> OutputParameters,
        IReadOnlyList<string> SubRoutines);

    private FirebirdDebugExecutor(DebugSessionConnection session, Encoding fallback)
    {
        _session = session;
        _fallback = fallback;
    }

    /// <summary>Creates the executor for a standalone routine (the root frame): resolves its frame variable
    /// templates (verbatim declarations R3 + base types R2) from metadata once, then registers its context.
    /// <paramref name="source"/> is the routine's full source (the span backing for fragments + declarations),
    /// <paramref name="body"/> its parsed body (the context key), <paramref name="model"/> its semantic model
    /// (the read/write sets consume it). <paramref name="fallback"/> is the source-blob decode fallback
    /// (UTF-8-first, then this) used when a stepped-into callee's source is reconstructed (D8).</summary>
    public static async Task<FirebirdDebugExecutor> CreateAsync(
        DebugSessionConnection session,
        string? routineName,
        string source,
        BlockStatement body,
        SemanticModel model,
        Encoding fallback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fallback);

        var layout = await FirebirdDebugMetadata
            .BuildFrameVariablesAsync(session, routineName, body, source, cancellationToken)
            .ConfigureAwait(false);
        var executor = new FirebirdDebugExecutor(session, fallback);
        executor.Register(body, source, model, layout.Variables, layout.OutputParameters);
        return executor;
    }

    // Registers a routine's context under its body node. Called for the root (CreateAsync) and, in seam b
    // part 2, for each stepped-into stored routine (ResolveRoutine). Idempotent for a body already known
    // (recursion re-resolves the same routine — keep the first context).
    private void Register(
        BlockStatement body, string source, SemanticModel model,
        IReadOnlyList<HarnessVariable> templates, IReadOnlyList<string> outputs)
    {
        if (_contexts.ContainsKey(body)) return;
        // R5 (§3.4): the routine's in-scope local sub-routine declarations, carried verbatim into every harness
        // for this routine so a statement that calls a local F()/P() binds to the local, never a like-named
        // global (a §F violation). Empty for a routine with no sub-routines (all D2–D8 routines) — no harness
        // change there. Extracted from the AST the parser already built (Contract #1).
        var subRoutines = PsqlDeclarationExtractor.Extract(body, source).SubRoutines;
        _contexts[body] = new RoutineContext(
            source, model, templates, new HashSet<string>(outputs, StringComparer.OrdinalIgnoreCase), subRoutines);
    }

    // The context for the routine the given frame activates — keyed by its Body. Every step / condition /
    // evaluation / cursor open is scoped to the frame's own routine (D8: a call stack of distinct routines).
    private RoutineContext Ctx(Frame frame)
        => _contexts.TryGetValue(frame.Body, out var ctx)
            ? ctx
            : throw new InvalidOperationException(
                $"Debug: no routine context registered for frame '{frame.RoutineName}'.");

    // ── IDebugExecutor ──────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public StatementOutcome ExecuteStatement(IExecutableStatement statement, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(frame);

        var ctx = Ctx(frame);

        // SUSPEND is control flow, not server semantics: it yields the current output-parameter values as the
        // routine's output row. Emit it client-side (no harness, no round-trip) — §3.1 (client owns control).
        if (statement is PsqlLeafStatement { Kind: PsqlLeafKind.Suspend })
        {
            return StatementOutcome.Suspended(SnapshotOutputs(ctx, frame));
        }

        var node = AsNode(statement);
        var (reads, writes) = ResolveReadWrite(ctx, node, frame);
        var request = new HarnessRequest
        {
            Fragment = Slice(ctx.Source, node),
            Mode = HarnessMode.Statement,
            Variables = BindValues(ctx, frame),
            Reads = reads,
            Writes = writes,
            SubRoutines = ctx.SubRoutines,
        };

        try
        {
            var run = Await(RunHarnessAsync(HarnessBuilder.Build(request), CancellationToken.None));
            return StatementOutcome.Normal(run.Writes);
        }
        catch (FbException ex)
        {
            return StatementOutcome.Raised(DebugErrorMapper.FromFirebird(ex));
        }
    }

    /// <inheritdoc/>
    public ConditionOutcome EvaluateCondition(IExecutableStatement owner, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(frame);

        var ctx = Ctx(frame);
        var node = AsNode(owner);
        var (reads, _) = ResolveReadWrite(ctx, node, frame);
        var request = new HarnessRequest
        {
            Fragment = ConditionExpression(ctx.Source, node),
            Mode = HarnessMode.Expression,
            ExpressionResultType = BooleanResultType,
            Variables = BindValues(ctx, frame),
            Reads = reads,
            SubRoutines = ctx.SubRoutines,
        };

        try
        {
            var run = Await(RunHarnessAsync(HarnessBuilder.Build(request), CancellationToken.None));
            object? value = run.ResultValue;
            return value is null or DBNull ? new ConditionOutcome(null) : ConditionOutcome.Of(Convert.ToBoolean(value));
        }
        catch (FbException ex)
        {
            return ConditionOutcome.Raised(DebugErrorMapper.FromFirebird(ex));
        }
    }

    /// <inheritdoc/>
    /// <remarks>D5 (§9.5): the user fragment is run through the SAME harness as a step — the one engine
    /// behind Evaluate / Watches / Immediate. It has no AST node, so the injected read/write set is the §3.5
    /// "inject all in-scope" primitive (<see cref="ReadWriteSetAnalyzer.InScopeLocals"/>). An Expression is
    /// evaluated into a text result column; a Statement runs verbatim and its frame write-back is returned
    /// (the session applies it — the Immediate window operates on the live frame).</remarks>
    public EvaluationResult Evaluate(EvaluationRequest request, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(frame);

        var ctx = Ctx(frame);
        var names = ReadWriteSetAnalyzer.InScopeLocals(ctx.Model, request.ScopeOffset);
        if (names.Count == 0)
        {
            // At/before an offset with no locals in scope — inject every known frame variable (still §3.5).
            names = AllTemplateNames(ctx);
        }

        bool expression = request.Kind == EvaluationKind.Expression;
        var harness = HarnessBuilder.Build(new HarnessRequest
        {
            Fragment = request.Fragment,
            Mode = expression ? HarnessMode.Expression : HarnessMode.Statement,
            ExpressionResultType = expression ? EvaluationResultType : null,
            Variables = BindValues(ctx, frame),
            Reads = names,
            Writes = expression ? Array.Empty<string>() : names, // an expression writes nothing; a statement may
            SubRoutines = ctx.SubRoutines,
        });

        try
        {
            var run = Await(RunHarnessAsync(harness, CancellationToken.None));
            return EvaluationResult.Ok(harness.Sql, run.ResultValue, run.Writes);
        }
        catch (FbException ex)
        {
            return EvaluationResult.Failed(harness.Sql, DebugErrorMapper.FromFirebird(ex));
        }
    }

    private static IReadOnlyList<string> AllTemplateNames(RoutineContext ctx)
    {
        var names = new List<string>(ctx.VariableTemplates.Count);
        foreach (var t in ctx.VariableTemplates) names.Add(t.Name);
        return names;
    }

    /// <inheritdoc/>
    /// <remarks>D6 (§7): the loop's cursor query is opened as a <b>real DSQL cursor</b> on the session
    /// connection, in the debug transaction, and fetched one row per iteration (the Cursor Bridge). The query
    /// text + bind parameters are built purely by <see cref="CursorBridge"/> (frame refs rewritten to
    /// positional <c>?</c> by resolved span); the ordered parameter names are bound from the current frame
    /// here. A <c>FOR EXECUTE STATEMENT</c> (no static <see cref="ForSelectStatement.Query"/>) is a §F boundary
    /// — refused with a clear message rather than guessed.</remarks>
    public IDebugCursor OpenCursor(ForSelectStatement loop, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(frame);
        if (loop.Query is null)
            throw new NotSupportedException(
                "Debug (D6): a FOR EXECUTE STATEMENT (dynamic) cursor cannot be stepped — step over the loop.");

        var plan = CursorBridge.Build(Ctx(frame).Source, loop);
        var values = new object?[plan.ParameterNames.Count];
        for (int i = 0; i < values.Length; i++)
        {
            frame.TryResolveValue(plan.ParameterNames[i], out var value);
            values[i] = value;
        }
        return Await(OpenCursorAsync(plan, values, CancellationToken.None));
    }

    private async Task<IDebugCursor> OpenCursorAsync(CursorQueryPlan plan, object?[] values, CancellationToken cancellationToken)
    {
        var gate = _session.CommandLock; // capture once (#98/#120) — one wire op (the OPEN), then released
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        FbCommand? cmd = null;
        try
        {
            cmd = _session.Connection.CreateCommand();
            cmd.CommandText = plan.Sql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _session.Transaction;
            foreach (var v in values) cmd.Parameters.Add(new FbParameter { Value = v ?? DBNull.Value });
            var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return new CursorHandle(_session, cmd, reader, plan.IntoTargets);
        }
        catch
        {
            if (cmd is not null) await cmd.DisposeAsync().ConfigureAwait(false); // don't leak the command on a failed open
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    /// <remarks>D8 (§5): resolves a standalone <c>EXECUTE PROCEDURE</c> call for step-into — fetches the
    /// callee's source (<see cref="FirebirdDdlReader"/>, on the debug session), parses it into a body +
    /// semantic model (gotcha #238: the whole <c>CREATE PROCEDURE</c>, so its declares are in scope), resolves
    /// its frame variable templates (R2/R3) via <see cref="FirebirdDebugMetadata"/>, <b>evaluates the call's
    /// arguments in the CALLER frame through a typed harness</b> to seed the callee's input parameters, and
    /// registers the callee's context. Returns null — so the interpreter runs the call in place (step-over,
    /// 100% faithful §5.3) — for any call it cannot faithfully descend into: a non-<c>EXECUTE PROCEDURE</c>
    /// step point, a call with no readable name, a package/qualified name (D11), or a callee whose source /
    /// metadata cannot be read or parsed. A local sub-routine (a closure) is D9; here every resolved callee is
    /// a <b>stored</b> routine (a closed scope — <see cref="DebugRoutine.LexicalParent"/> stays null).</remarks>
    public DebugRoutine? ResolveRoutine(IExecutableStatement call, Frame frame)
    {
        if (call is not ExecuteProcedureStatement exec) return null;
        if (string.IsNullOrWhiteSpace(exec.ProcedureName)) return null;

        // D9: a LOCAL sub-procedure visible from this frame's lexical scope resolves to a real frame WITHOUT a
        // server source fetch — its body is already parsed (part of the enclosing routine's AST) and its
        // parameter types come from the AST header (a local routine is not a catalog object). A local FUNCTION
        // is never here (it is called inside an expression, not as an EXECUTE PROCEDURE step point).
        if (TryFindLocalProcedure(exec.ProcedureName!, frame) is { } local)
        {
            try
            {
                return Await(BuildLocalRoutineAsync(
                    exec, frame, local.Declaration, local.DeclaringFrame, CancellationToken.None));
            }
            catch (FbException)
            {
                return null; // param base-type derivation unreadable → step over in place (§5.3), never guess
            }
        }

        // A dotted (package.procedure) callee is D11 — step over it for now (§F: faithful, just no descent).
        if (exec.ProcedureName!.Contains('.')) return null;

        try
        {
            return Await(ResolveRoutineAsync(exec, frame, CancellationToken.None));
        }
        catch (FbException)
        {
            return null; // callee source / metadata unreadable → step over in place (§5.3), never guess
        }
    }

    // Finds a LOCAL sub-procedure named <paramref name="name"/> visible from <paramref name="frame"/>, walking
    // the lexical scope chain (this frame's routine body, then its declaring frame's, …) exactly as name
    // resolution does (spec §6). Returns the declaration + the frame that declares it (the callee's lexical
    // parent). Only a PROCEDURE with a real Body qualifies: a local FUNCTION is called in an expression (never
    // an EXECUTE PROCEDURE step point), and a forward declaration (null body) is not runnable — the real
    // definition carries the body.
    private static (SubroutineDeclaration Declaration, Frame DeclaringFrame)? TryFindLocalProcedure(
        string name, Frame frame)
    {
        for (var f = frame; f is not null; f = f.LexicalParent)
        {
            foreach (var r in f.Body.LocalRoutines)
            {
                if (r.Kind == SubroutineKind.Procedure && r.Body is not null
                    && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return (r, f);
                }
            }
        }
        return null;
    }

    private async Task<DebugRoutine?> BuildLocalRoutineAsync(
        ExecuteProcedureStatement exec, Frame callerFrame, SubroutineDeclaration routine, Frame declaringFrame,
        CancellationToken cancellationToken)
    {
        var body = routine.Body!;
        var callerCtx = Ctx(callerFrame);

        // Frame templates from the AST header (params + RETURNS) + the sub-routine body's locals — NOT from
        // RDB$PROCEDURE_PARAMETERS (a local routine is not a catalog object; this is the one new metadata path
        // of D9 seam a part 2). Source + model are the ENCLOSING routine's: a local sub-routine's spans live in
        // the enclosing source, and its scope is a child of the enclosing model's scope tree.
        var layout = await FirebirdDebugMetadata
            .BuildLocalRoutineFrameVariablesAsync(_session, routine, callerCtx.Source, cancellationToken)
            .ConfigureAwait(false);

        // Evaluate the call's arguments in the CALLER frame (the SAME harness as a step, Contract #4) → seed the
        // callee's input parameters positionally (the D8 mechanism, reused unchanged).
        var initialValues = await SeedInputParametersAsync(
            exec, callerFrame, layout.InputParameters, cancellationToken).ConfigureAwait(false);

        Register(body, callerCtx.Source, callerCtx.Model, layout.Variables, layout.OutputParameters);

        // §6.3 closure gate (MEASURED — spec §15.7): FB3 sub-routines are CLOSED scopes (LexicalParent = null,
        // like a stored callee — an outer reference won't even compile there, so a closed frame is 100% faithful
        // by construction); FB5 are true closures (LexicalParent = the declaring frame — outer reads/writes
        // resolve up the chain). FB4 is treated as closed (conservative — a documented §F boundary, unverified).
        // A self-contained local routine (seam a part 2's zoo — no outer-variable references) does not exercise
        // the closure, so the choice is behaviourally inert here but set correctly for seam b's closure harness.
        int serverMajor = FirebirdDdlReader.ParseServerMajor(_session.Connection.ServerVersion);
        Frame? lexicalParent = serverMajor >= 5 ? declaringFrame : null;

        return new DebugRoutine(
            routine.Name ?? "(local routine)", body, initialValues, layout.OutputParameters,
            lexicalParent: lexicalParent, source: callerCtx.Source, model: callerCtx.Model);
    }

    private async Task<DebugRoutine?> ResolveRoutineAsync(
        ExecuteProcedureStatement exec, Frame callerFrame, CancellationToken cancellationToken)
    {
        string name = exec.ProcedureName!;
        string source = await LoadProcedureSourceAsync(name, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(source)) return null;

        // Strict whole-routine parse (gotcha #238): CREATE PROCEDURE stays one DdlStatement whose Body is
        // bound with its declares in scope. Null body (not a PSQL procedure / unparsed) → step over.
        var model = SemanticModel.Build(SqlParser.Parse(source).Root);
        BlockStatement? body = null;
        foreach (var st in model.Syntax.Statements)
        {
            if (st is DdlStatement { Body: { } b }) { body = b; break; }
        }
        if (body is null) return null;

        var layout = await FirebirdDebugMetadata
            .BuildFrameVariablesAsync(_session, name, body, source, cancellationToken)
            .ConfigureAwait(false);

        // Evaluate the call's arguments in the CALLER frame (typed as the callee's input params) → seed them.
        var initialValues = await SeedInputParametersAsync(
            exec, callerFrame, layout.InputParameters, cancellationToken).ConfigureAwait(false);

        Register(body, source, model, layout.Variables, layout.OutputParameters);
        return new DebugRoutine(
            name, body, initialValues, layout.OutputParameters, lexicalParent: null, source: source, model: model);
    }

    // Reconstructs the callee's CREATE OR ALTER PROCEDURE source on the DEBUG session (its own attachment +
    // transaction), holding the session command lock across the multi-command reconstruction (#98/#120/#236 —
    // captured once). Reading committed procedure source on the debug tx is consistent with the session's
    // isolation and keeps everything on the one debug attachment.
    private async Task<string> LoadProcedureSourceAsync(string name, CancellationToken cancellationToken)
    {
        int serverMajor = FirebirdDdlReader.ParseServerMajor(_session.Connection.ServerVersion);
        var gate = _session.CommandLock;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await FirebirdDdlReader.BuildProcedureSourceAsync(
                _session.Connection, _session.Transaction, name, serverMajor, _fallback, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    // Evaluates each call argument in the CALLER frame — through the SAME harness as a step (no second
    // evaluator, Contract #4) — assigning it to a synthetic result variable typed as the corresponding callee
    // INPUT parameter's base type (R2), so the server computes each argument with full fidelity (types,
    // evaluation order, NULL) and returns it typed. The values seed the callee frame's input parameters
    // positionally. Over-injects the caller's in-scope locals (§3.5 safe fallback) so any variable an
    // argument references is present. Zips to the shorter of (args, input params): a call omitting trailing
    // defaulted params seeds only the provided ones (the rest start unset — a documented §F boundary, since a
    // parameter default is evaluated by the callee's own signature, not reconstructed here).
    private async Task<IReadOnlyDictionary<string, object?>?> SeedInputParametersAsync(
        ExecuteProcedureStatement exec, Frame callerFrame,
        IReadOnlyList<HarnessVariable> inputParameters, CancellationToken cancellationToken)
    {
        int n = Math.Min(exec.Arguments.Count, inputParameters.Count);
        if (n == 0) return null;

        var callerCtx = Ctx(callerFrame);

        // Synthetic result variables (ET_ARG_i), typed as the callee input params, plus the caller's own
        // variables (so the argument expressions resolve). The synthetic vars are the write set; the caller's
        // in-scope locals are the read set (injected).
        var variables = new List<HarnessVariable>(BindValues(callerCtx, callerFrame));
        var argNames = new string[n];
        var fragment = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            string argVar = ArgVarPrefix + i;
            argNames[i] = argVar;
            string baseType = inputParameters[i].BaseType;
            variables.Add(new HarnessVariable(argVar, $"DECLARE {argVar} {baseType};", baseType));
            string argText = RewriteColonRefsToBare(
                callerCtx.Source, exec.Tokens, exec.Arguments[i].Start, exec.Arguments[i].Length);
            fragment.Append(argVar).Append(" = ").Append(argText.Trim()).Append(';');
        }

        var reads = ReadWriteSetAnalyzer.InScopeLocals(callerCtx.Model, exec.Start);
        var harness = HarnessBuilder.Build(new HarnessRequest
        {
            Fragment = fragment.ToString(),
            Mode = HarnessMode.Statement,
            Variables = variables,
            Reads = reads,
            Writes = argNames,
            SubRoutines = callerCtx.SubRoutines, // an argument may call a local F()/P() (R5)
        });

        var run = await RunHarnessAsync(harness, cancellationToken).ConfigureAwait(false);

        var seeded = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < n; i++)
        {
            object? value = run.Writes is { } w && w.TryGetValue(argNames[i], out var v) ? v : null;
            seeded[inputParameters[i].Name] = value;
        }
        return seeded;
    }

    // Rewrites a call argument's source text for use as the RHS of a PSQL assignment in the seeding harness:
    // each :name / @name frame-variable reference (a Parameter token — Firebird's unambiguous variable syntax
    // in a call's argument list) is rewritten to its BARE name, because the colon/at form is a SQL error in a
    // PSQL expression (verified live: `x = :y;` → SQL -104 "token unknown"). Rewritten BY SPAN over the
    // statement's own tokens (never text search — a ':' inside a string literal is a String token, untouched),
    // mirroring CursorBridge's colon rewrite (there → positional '?'; here → bare name). Everything else is
    // copied verbatim, so a literal / arithmetic argument (SP(:P + 1, 10)) is preserved exactly.
    private static string RewriteColonRefsToBare(
        string source, IReadOnlyList<SqlToken> tokens, int argStart, int argLength)
    {
        int start = Math.Clamp(argStart, 0, source.Length);
        int end = Math.Clamp(argStart + argLength, start, source.Length);
        var sb = new StringBuilder(end - start + 8);
        int cursor = start;
        foreach (var tok in tokens)
        {
            if (tok.Kind != TokenKind.Parameter || tok.Start < start || tok.End > end) continue;
            if (tok.Start < cursor) continue; // defensive: disjoint tokens, should not overlap
            sb.Append(source, cursor, tok.Start - cursor);
            sb.Append(tok.Text.TrimStart(':', '@')); // the bare variable name
            cursor = tok.End;
        }
        sb.Append(source, cursor, end - cursor);
        return sb.ToString();
    }

    /// <inheritdoc/>
    public void EnterFrameSavepoint(string name) => Await(_session.SetSavepointAsync(name));

    /// <inheritdoc/>
    public void LeaveFrameSavepoint(string name) => Await(_session.ReleaseSavepointAsync(name));

    /// <inheritdoc/>
    public void RollbackFrameSavepoint(string name) => Await(_session.RollbackToSavepointAsync(name));

    // ── Harness execution ───────────────────────────────────────────────────────────────────────────

    private readonly record struct HarnessRun(
        IReadOnlyDictionary<string, object?>? Writes, object? ResultValue);

    private async Task<HarnessRun> RunHarnessAsync(HarnessResult harness, CancellationToken cancellationToken)
    {
        bool selectable = harness.WriteBacks.Count > 0 || harness.ResultColumn is not null;

        await _session.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = _session.Connection.CreateCommand();
            cmd.CommandText = harness.Sql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _session.Transaction;
            foreach (var value in harness.Parameters)
            {
                cmd.Parameters.Add(new FbParameter { Value = value ?? DBNull.Value });
            }

            if (!selectable)
            {
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return new HarnessRun(null, null);
            }

            Dictionary<string, object?>? writes = harness.WriteBacks.Count > 0
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                : null;
            object? resultValue = null;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (harness.ResultColumn is not null)
                {
                    resultValue = ReadColumn(reader, harness.ResultColumn);
                }
                foreach (var wb in harness.WriteBacks)
                {
                    writes![wb.Variable] = ReadColumn(reader, wb.Column);
                }
            }
            return new HarnessRun(writes, resultValue);
        }
        finally
        {
            _session.CommandLock.Release();
        }
    }

    private static object? ReadColumn(IDataReader reader, string column)
    {
        int i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? null : reader.GetValue(i);
    }

    // ── Read/write set (§3.5) ───────────────────────────────────────────────────────────────────────

    // The precise read/write set narrows the injected payload (§3.5). But a reused SELECT … INTO surfaces
    // NO local references — the query binder records its FROM/columns, not the :colon-refs in the WHERE nor
    // the INTO targets (verified: such a statement analyses to empty reads AND empty writes, while a token-
    // walked INSERT/assignment/IF surfaces its refs correctly). Injecting a wrong narrow set there would drop
    // the INTO write-back (the variable the statement exists to set) — a §F divergence. So when the model
    // surfaces nothing, fall back to §3.5's named "inject all in-scope" primitive (correct, chattier), never
    // a guess. A statement that genuinely touches no local (e.g. bare EXCEPTION) is over-included harmlessly.
    private static (IReadOnlyList<string> Reads, IReadOnlyList<string> Writes) ResolveReadWrite(
        RoutineContext ctx, SqlNode node, Frame frame)
    {
        // D9 seam b Part 2: pass the in-scope local sub-routine catalog so a statement that CALLS a local
        // routine folds in that callee's transitively-captured variables (§3.5). For a routine with no local
        // sub-routines the catalog is empty and Analyze is exactly the direct-reference set (D2–D8 unchanged).
        var rw = ReadWriteSetAnalyzer.Analyze(node, ctx.Model, BuildSubroutineCatalog(frame));
        if (rw.Reads.Count == 0 && rw.Writes.Count == 0)
        {
            var all = ReadWriteSetAnalyzer.InScopeLocals(ctx.Model, node.Start);
            return (all, all);
        }
        return (rw.Reads, rw.Writes);
    }

    // The local sub-routines in scope at the frame — its own routine body's, then each enclosing (lexical)
    // frame's, up the closure chain (spec §6). Nearest scope first (an inner declaration shadows a like-named
    // outer). Empty for a routine that declares none (D2–D8), so the fixpoint is a no-op there.
    private static SubroutineCatalog BuildSubroutineCatalog(Frame frame)
    {
        List<SubroutineDeclaration>? routines = null;
        for (var f = frame; f is not null; f = f.LexicalParent)
        {
            foreach (var r in f.Body.LocalRoutines)
            {
                (routines ??= new List<SubroutineDeclaration>()).Add(r);
            }
        }
        return routines is null ? SubroutineCatalog.Empty : new SubroutineCatalog(routines);
    }

    // ── Frame → harness variables ───────────────────────────────────────────────────────────────────

    // Every in-scope variable is declared in the harness (verbatim, R3); its current value (if any) rides
    // along so HarnessBuilder can inject the reads (R1 skips a null/absent value). Over-declaring a variable
    // the fragment does not use is harmless — the read/write set narrows what is injected/returned (§3.5).
    // <para>
    // <b>Closure capture (D9 seam b, §6.2b).</b> A local sub-routine's frame is a closure over the declaring
    // frame (FB5), so a statement in its body may reference an OUTER variable. Those variables are NOT in this
    // frame's own templates, so — beyond this frame's own declarations — every ancestor frame's variables up
    // the lexical chain (<see cref="Frame.LexicalParent"/>) are also declared here (verbatim) with their
    // current value, so the harness can declare + inject + write them back. An inner declaration SHADOWS a
    // like-named outer (first-seen wins, this frame first). For a non-closure frame (lexical parent null — the
    // root, a stored callee) the chain loop does nothing, so this is behaviourally identical to before.
    // </para>
    private IReadOnlyList<HarnessVariable> BindValues(RoutineContext ctx, Frame frame)
    {
        var bound = new List<HarnessVariable>(ctx.VariableTemplates.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddTemplates(ctx.VariableTemplates, frame, bound, seen);
        for (var f = frame.LexicalParent; f is not null; f = f.LexicalParent)
        {
            if (_contexts.TryGetValue(f.Body, out var parentCtx))
            {
                AddTemplates(parentCtx.VariableTemplates, frame, bound, seen);
            }
        }
        return bound;
    }

    private static void AddTemplates(
        IReadOnlyList<HarnessVariable> templates, Frame frame, List<HarnessVariable> bound, HashSet<string> seen)
    {
        foreach (var template in templates)
        {
            if (!seen.Add(template.Name)) continue; // an inner declaration shadows a like-named outer one
            bound.Add(frame.TryResolveValue(template.Name, out var value)
                ? template with { Value = value, HasValue = true }
                : template);
        }
    }

    private static IReadOnlyDictionary<string, object?> SnapshotOutputs(RoutineContext ctx, Frame frame)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ctx.OutputParameters)
        {
            row[name] = frame.TryResolveValue(name, out var v) ? v : null;
        }
        return row;
    }

    // ── Source spans ────────────────────────────────────────────────────────────────────────────────

    private static string Slice(string source, SqlNode node) => Slice(source, node.Start, node.Length);

    private static string Slice(string source, int nodeStart, int nodeLength)
    {
        int start = Math.Clamp(nodeStart, 0, source.Length);
        int length = Math.Clamp(nodeLength, 0, source.Length - start);
        return source.Substring(start, length);
    }

    // The parenthesised condition of an IF/WHILE header — the first top-level (…) group after the keyword.
    // Firebird requires the condition in parens (IF (<cond>) THEN / WHILE (<cond>) DO), so the first '(' opens
    // it and its match closes it. Read from the node's tokens (never re-parsed) and sliced verbatim from
    // source; the whole "(<cond>)" is a valid boolean expression for the Expression-mode harness.
    private static string ConditionExpression(string source, SqlNode node)
    {
        if (node is PsqlStatement psql)
        {
            var tokens = psql.Tokens;
            int depth = 0, open = -1;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.LParen)
                {
                    if (open < 0) open = i;
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.RParen)
                {
                    depth--;
                    if (depth == 0 && open >= 0)
                    {
                        int s = tokens[open].Start;
                        int e = tokens[i].End;
                        if (s >= 0 && e <= source.Length && e > s)
                        {
                            return source.Substring(s, e - s);
                        }
                        break;
                    }
                }
            }
        }
        // Fallback (malformed / unexpected shape): the whole node text. §F: correctness over cleverness —
        // Firebird will report a syntax error rather than the debugger silently guessing a boolean.
        return Slice(source, node);
    }

    private static SqlNode AsNode(IExecutableStatement statement)
        => statement as SqlNode
           ?? throw new InvalidOperationException(
               $"Debug: step point {statement.GetType().Name} is not a source node.");

    // ── Sync-over-async bridge (see the class remark) ───────────────────────────────────────────────

    private static void Await(Task task) => task.GetAwaiter().GetResult();

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
