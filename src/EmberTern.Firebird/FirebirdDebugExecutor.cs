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
        IReadOnlyList<string> SubRoutines,
        // The trigger context (Stage X / D10) — non-null ONLY for a trigger's root frame. When present, every
        // statement/condition of this routine is routed through ContextSubstitution before the harness so its
        // NEW/OLD columns and INSERTING/UPDATING/DELETING predicates become synthetic frame variables + literals
        // (spec §8.1). A stepped-into callee (a stored/local routine) has no trigger context (NEW/OLD are not in
        // scope there), so this stays null for every non-root frame — leaving D8/D9 paths untouched.
        TriggerContext? Trigger = null,
        // The package a member frame belongs to (Stage X / D11) — non-null ONLY for a package member frame. An
        // unqualified sibling call inside such a frame is resolved against <see cref="PackageMembers"/> (the
        // package's routines, parsed once); and every package routine is declared as a harness sub-routine (R5,
        // via <see cref="SubRoutines"/>) so a sibling call — public OR private — runs inside the harness like a
        // D9 local routine (a private routine is not DSQL-callable, §15.12). Null for every non-package frame,
        // leaving D8/D9 paths untouched.
        string? PackageName = null,
        IReadOnlyList<SubroutineDeclaration>? PackageMembers = null);

    // A package's parsed body (Stage X / D11), cached per package name for the session: the raw body source
    // (RDB$PACKAGE_BODY_SOURCE — the frame-source backing + the R5 sub-routine declarations) and its member
    // routines (SqlParser.ParsePackageBodyMembers). Null = the package has no readable body (→ step over).
    private readonly Dictionary<string, PackageBody?> _packages = new(StringComparer.OrdinalIgnoreCase);
    private sealed record PackageBody(string Source, IReadOnlyList<SubroutineDeclaration> Members);

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
        => await CreateAsync(session, routineName, source, body, model, fallback, trigger: null, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Creates the executor for a <b>trigger</b> root frame (Stage X / D10): as the standalone overload,
    /// but the root routine's <c>NEW</c>/<c>OLD</c> context columns become additional frame variables — their
    /// types resolved from the trigger's target table (<see cref="FirebirdDebugMetadata.BuildTriggerContextVariablesAsync"/>)
    /// and merged into the frame templates — and the <paramref name="trigger"/> context is registered on the
    /// root so every statement/condition is routed through <see cref="ContextSubstitution"/>. A trigger has no
    /// stored parameters (it is not a procedure), so the parameter query is skipped.</summary>
    public static async Task<FirebirdDebugExecutor> CreateAsync(
        DebugSessionConnection session,
        string? routineName,
        string source,
        BlockStatement body,
        SemanticModel model,
        Encoding fallback,
        TriggerContext? trigger,
        CancellationToken cancellationToken = default,
        string? packageName = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fallback);

        var executor = new FirebirdDebugExecutor(session, fallback);

        // D11 seam C: a package member launched as the debug ROOT. Built the SAME way a stepped-into package
        // member is (seam B): package-keyed catalog params (the ONE catalog difference), and the package's
        // routines declared as harness sub-routines (R5) so a sibling call — public OR private — resolves inside
        // the harness like a D9 local routine (§15.12), with the package + its members carried on the frame's
        // context so an unqualified sibling resolves. <paramref name="source"/> is the member reconstructed as a
        // standalone CREATE PROCEDURE (the App/probe source provider does the same reconstruction). A closed
        // scope (LexicalParent null) — Execute/EvaluateCondition/BindValues are untouched, as for a step-into
        // package frame. (trigger + packageName are mutually exclusive.)
        if (packageName is not null)
        {
            var pkgLayout = await FirebirdDebugMetadata
                .BuildFrameVariablesAsync(session, routineName, body, source, cancellationToken, packageName)
                .ConfigureAwait(false);
            var pkg = await executor.PackageBodyFor(packageName, cancellationToken).ConfigureAwait(false);
            if (pkg is not null)
            {
                executor.RegisterPackageMember(
                    body, source, model, pkgLayout.Variables, pkgLayout.OutputParameters, packageName, pkg);
            }
            else
            {
                // No readable package body → register as a plain routine (sibling calls won't resolve → step
                // over, faithful). Reaching launch without a body is not expected (the source was reconstructed
                // from it), but never guess (§F).
                executor.Register(body, source, model, pkgLayout.Variables, pkgLayout.OutputParameters);
            }
            return executor;
        }

        // A trigger is not a catalog procedure, so skip the RDB$PROCEDURE_PARAMETERS query (it has none — passing
        // null avoids a pointless round-trip and any name collision with a like-named procedure).
        var layout = await FirebirdDebugMetadata
            .BuildFrameVariablesAsync(session, trigger is null ? routineName : null, body, source, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<HarnessVariable> templates = layout.Variables;
        if (trigger is not null)
        {
            var contextVars = await FirebirdDebugMetadata
                .BuildTriggerContextVariablesAsync(session, trigger.TargetTable, trigger.Columns, cancellationToken)
                .ConfigureAwait(false);
            var merged = new List<HarnessVariable>(layout.Variables.Count + contextVars.Count);
            merged.AddRange(layout.Variables);
            merged.AddRange(contextVars);
            templates = merged;
        }

        executor.Register(body, source, model, templates, layout.OutputParameters, trigger);
        return executor;
    }

    // Registers a routine's context under its body node. Called for the root (CreateAsync) and, in seam b
    // part 2, for each stepped-into stored routine (ResolveRoutine). Idempotent for a body already known
    // (recursion re-resolves the same routine — keep the first context).
    private void Register(
        BlockStatement body, string source, SemanticModel model,
        IReadOnlyList<HarnessVariable> templates, IReadOnlyList<string> outputs, TriggerContext? trigger = null)
    {
        if (_contexts.ContainsKey(body)) return;
        // R5 (§3.4): the routine's in-scope local sub-routine declarations, carried verbatim into every harness
        // for this routine so a statement that calls a local F()/P() binds to the local, never a like-named
        // global (a §F violation). Empty for a routine with no sub-routines (all D2–D8 routines) — no harness
        // change there. Extracted from the AST the parser already built (Contract #1).
        var subRoutines = PsqlDeclarationExtractor.Extract(body, source).SubRoutines;
        _contexts[body] = new RoutineContext(
            source, model, templates, new HashSet<string>(outputs, StringComparer.OrdinalIgnoreCase), subRoutines, trigger);
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
        string fragment = Slice(ctx.Source, node);

        // D10: a trigger frame's NEW/OLD columns and INSERTING/UPDATING/DELETING predicates are rewritten to
        // synthetic frame variables + boolean literals (spec §8.1) before the harness, and the context reads it
        // injects / writes it returns are unioned into the read/write set. Non-trigger frames are unchanged.
        if (ctx.Trigger is not null)
        {
            // A context reference must be colon-prefixed exactly where Firebird would read a bare name as a
            // COLUMN — inside an embedded DSQL query (gotchas #247/#248): the whole statement when it IS a DSQL
            // statement (SELECT…INTO / INSERT / UPDATE / DELETE / MERGE, not a PSQL leaf), or each embedded
            // scalar/EXISTS subquery inside a PSQL statement (a subquery in an assignment RHS / a condition). A
            // PSQL l-value / expression stays bare (a colon there is SQL -104). Decided per-reference.
            var rewrite = ContextSubstitution.Substitute(
                ctx.Model, ctx.Source, SpanOf(node), ctx.Trigger, ColonRegions(node));
            fragment = rewrite.Fragment;
            reads = Union(reads, rewrite.ContextReads);
            writes = Union(writes, rewrite.ContextWrites);
        }

        var request = new HarnessRequest
        {
            Fragment = fragment,
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

        // D10: a trigger frame's IF/WHILE condition may reference NEW/OLD or a predicate — substitute over the
        // condition region (the paren group) before evaluating it, unioning the context reads it injects.
        string condition;
        if (ctx.Trigger is not null)
        {
            var (s, e) = ConditionBounds(node);
            if (s >= 0 && e <= ctx.Source.Length && e > s)
            {
                // A condition is a PSQL expression, so its NEW/OLD refs are bare — EXCEPT any inside an embedded
                // subquery (IF (x = (select … where c = new.col))), which must be colon-prefixed (#248).
                var rewrite = ContextSubstitution.Substitute(
                    ctx.Model, ctx.Source, TextSpan.FromBounds(s, e), ctx.Trigger, ColonRegions(node));
                condition = rewrite.Fragment;
                reads = Union(reads, rewrite.ContextReads);
            }
            else
            {
                condition = Slice(ctx.Source, node); // fallback (malformed shape) — §F, server reports it
            }
        }
        else
        {
            condition = ConditionExpression(ctx.Source, node);
        }

        var (value, error) = EvaluateExpression(ctx, frame, condition, BooleanResultType, reads);
        if (error is not null) return ConditionOutcome.Raised(error);
        return value is null or DBNull ? new ConditionOutcome(null) : ConditionOutcome.Of(Convert.ToBoolean(value));
    }

    /// <inheritdoc/>
    /// <remarks>D12 (§9.8.2): a breakpoint condition is a user-supplied boolean fragment (no AST node), so —
    /// exactly like <see cref="Evaluate"/> — its injected read set is the §3.5 in-scope-locals primitive at the
    /// breakpoint offset. It is then run through the very same typed <see cref="EvaluateExpression"/> path the
    /// <c>IF</c>/<c>WHILE</c> overload uses (<see cref="BooleanResultType"/>), and its result is interpreted
    /// identically: NULL → no branch (three-valued logic), otherwise <c>Convert.ToBoolean</c>. One engine, no
    /// second evaluator.</remarks>
    public ConditionOutcome EvaluateCondition(string fragment, int scopeOffset, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(frame);

        var ctx = Ctx(frame);
        var names = ReadWriteSetAnalyzer.InScopeLocals(ctx.Model, scopeOffset);
        if (names.Count == 0)
        {
            names = AllTemplateNames(ctx); // no locals in scope here — inject every known frame variable (§3.5)
        }

        var (value, error) = EvaluateExpression(ctx, frame, fragment, BooleanResultType, names);
        if (error is not null) return ConditionOutcome.Raised(error);
        return value is null or DBNull ? new ConditionOutcome(null) : ConditionOutcome.Of(Convert.ToBoolean(value));
    }

    // The one typed-expression evaluator (D9 seam c): runs a fragment through the Expression Harness typed as
    // <paramref name="resultType"/> and returns (value, error). Shared by EvaluateCondition (BOOLEAN → branch
    // decision) and EvaluateReturn (the function's RETURNS base type → return value) — one server path, no
    // second evaluator (Contract #3/#4). The server computes the value; this never coerces or decides anything.
    private (object? Value, DebugError? Error) EvaluateExpression(
        RoutineContext ctx, Frame frame, string fragment, string resultType, IReadOnlyList<string> reads)
    {
        var request = new HarnessRequest
        {
            Fragment = fragment,
            Mode = HarnessMode.Expression,
            ExpressionResultType = resultType,
            Variables = BindValues(ctx, frame),
            Reads = reads,
            SubRoutines = ctx.SubRoutines,
        };

        try
        {
            var run = Await(RunHarnessAsync(HarnessBuilder.Build(request), CancellationToken.None));
            return (run.ResultValue, null);
        }
        catch (FbException ex)
        {
            return (null, DebugErrorMapper.FromFirebird(ex));
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

        var ctx = Ctx(frame);
        // D10 §F boundary (decision 2): a FOR SELECT cursor that references NEW/OLD cannot be stepped — the
        // cursor is a separately-opened DSQL statement where the harness's synthetic context variables do not
        // exist. Refuse clearly rather than open a partially-faithful cursor.
        if (ctx.Trigger is not null && QueryReferencesContext(ctx.Model, loop.Query))
            throw new NotSupportedException(
                "Debug (D10): a FOR SELECT cursor that references NEW/OLD is not supported in a trigger — step over the loop.");

        var plan = CursorBridge.Build(ctx.Source, loop);
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
        if (TryFindLocalRoutine(exec.ProcedureName!, frame, SubroutineKind.Procedure) is { } local)
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

        // D11: a package routine — a QUALIFIED PKG.PROC call, or an UNQUALIFIED sibling call from within a
        // package member frame. Resolved to a real frame the D8 way (reconstruct the member's source, catalog
        // params keyed by package, seed the args), with the package's routines declared as harness sub-routines
        // so a sibling — public OR private — runs inside the harness like a D9 local routine (§15.12). Not a
        // package call → fall through to the standalone (D8) path.
        if (TryResolvePackageCall(exec, frame) is { } packageResolution)
        {
            try
            {
                return Await(packageResolution);
            }
            catch (FbException)
            {
                return null; // package body / member metadata unreadable → step over in place (§5.3)
            }
        }

        try
        {
            return Await(ResolveRoutineAsync(exec, frame, CancellationToken.None));
        }
        catch (FbException)
        {
            return null; // callee source / metadata unreadable → step over in place (§5.3), never guess
        }
    }

    // Decides whether an EXECUTE PROCEDURE call is a package routine and, if so, returns its resolution task
    // (null = not a package call, fall through to the standalone D8 path). Two forms: a QUALIFIED PKG.PROC
    // call (exec.PackageName set — D11 seam A), and an UNQUALIFIED sibling call from within a package member
    // frame (the frame's context carries the package + its members). The local-sub-routine check runs first
    // (nearest scope shadows a sibling), so a same-named local wins.
    private Task<DebugRoutine?>? TryResolvePackageCall(ExecuteProcedureStatement exec, Frame frame)
    {
        if (exec.PackageName is { Length: > 0 } qualified)
        {
            return ResolvePackageMemberAsync(qualified, exec.ProcedureName!, exec, frame, CancellationToken.None);
        }

        var ctx = Ctx(frame);
        if (ctx.PackageName is { } pkg && ctx.PackageMembers is { } members
            && HasMember(members, exec.ProcedureName!, SubroutineKind.Procedure))
        {
            return ResolvePackageMemberAsync(pkg, exec.ProcedureName!, exec, frame, CancellationToken.None);
        }
        return null;
    }

    private static bool HasMember(IReadOnlyList<SubroutineDeclaration> members, string name, SubroutineKind kind)
    {
        foreach (var m in members)
        {
            if (m.Kind == kind && m.Body is not null
                && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    // Builds a stepped-into PACKAGE member frame (Stage X / D11). Maximal D8 reuse: the member is reconstructed
    // as a standalone CREATE PROCEDURE (its body source is a slice of RDB$PACKAGE_BODY_SOURCE) so the SAME parse
    // → scope-bound model + body, package-aware catalog params, and argument seeding as a stored routine apply.
    // The only package-specific state: the frame's context carries the package + its members so an unqualified
    // sibling resolves, and R5 declares every package routine in the harness (so a private sibling — not
    // DSQL-callable — runs inside the harness, the D9 mechanism, §15.12). LexicalParent stays null (a package
    // member is a closed scope — it sees no caller variables and packages have no package-level variables, §8.2).
    private async Task<DebugRoutine?> ResolvePackageMemberAsync(
        string packageName, string memberName, ExecuteProcedureStatement exec, Frame callerFrame,
        CancellationToken cancellationToken)
    {
        var pkg = await PackageBodyFor(packageName, cancellationToken).ConfigureAwait(false);
        if (pkg is null) return null; // no readable body → step over (§5.3)

        // Reconstruct the member as a standalone CREATE PROCEDURE and parse it exactly like a stored routine
        // (D8): "CREATE " + the member's own "PROCEDURE name(params) RETURNS(...) AS … BEGIN … END" source.
        // The reconstruction is the one shared owner (SqlParser) — the same call the root-launch path uses.
        string? memberSource = SqlParser.ReconstructPackageMemberSource(
            pkg.Source, pkg.Members, memberName, SubroutineKind.Procedure);
        if (memberSource is null) return null; // not a (runnable) member → step over
        var model = SemanticModel.Build(SqlParser.Parse(memberSource).Root);
        BlockStatement? body = null;
        foreach (var st in model.Syntax.Statements)
        {
            if (st is DdlStatement { Body: { } b }) { body = b; break; }
        }
        if (body is null) return null;

        // Frame templates from the catalog, keyed by package (the ONE difference from a standalone routine).
        var layout = await FirebirdDebugMetadata
            .BuildFrameVariablesAsync(_session, memberName, body, memberSource, cancellationToken, packageName)
            .ConfigureAwait(false);

        // Seed the callee's input params by evaluating the call's arguments in the CALLER frame (Contract #4 —
        // the same harness a stored/local step-into uses).
        var initialValues = await SeedInputParametersAsync(
            exec.Arguments, exec.Tokens, exec.Start, callerFrame, layout.InputParameters, cancellationToken)
            .ConfigureAwait(false);

        RegisterPackageMember(body, memberSource, model, layout.Variables, layout.OutputParameters, packageName, pkg);

        return new DebugRoutine(
            memberName, body, initialValues, layout.OutputParameters, lexicalParent: null, source: memberSource, model: model);
    }

    // Fetches + parses a package body once per session (cached). Null when the package has no readable body.
    private async Task<PackageBody?> PackageBodyFor(string packageName, CancellationToken cancellationToken)
    {
        if (_packages.TryGetValue(packageName, out var cached)) return cached;
        string? bodySource = await LoadPackageBodySourceAsync(packageName, cancellationToken).ConfigureAwait(false);
        PackageBody? info = string.IsNullOrWhiteSpace(bodySource)
            ? null
            : new PackageBody(bodySource!, SqlParser.ParsePackageBodyMembers(bodySource));
        _packages[packageName] = info;
        return info;
    }

    // Reads RDB$PACKAGE_BODY_SOURCE on the DEBUG session (its own attachment + tx), holding the session command
    // lock across the read (#98/#120/#236). Mirrors LoadProcedureSourceAsync.
    private async Task<string?> LoadPackageBodySourceAsync(string packageName, CancellationToken cancellationToken)
    {
        var gate = _session.CommandLock;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await FirebirdDdlReader.ReadPackageBodySourceAsync(
                _session.Connection, _session.Transaction, packageName, _fallback, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    // Registers a package member frame's context. Differs from Register only in that its R5 sub-routine set is
    // EVERY package routine (declared verbatim as a harness DECLARE sub-routine, so a sibling call resolves in
    // the harness — the D9 mechanism), plus the member's own local sub-routines, and it carries the package +
    // its members so an unqualified sibling call resolves.
    private void RegisterPackageMember(
        BlockStatement body, string source, SemanticModel model,
        IReadOnlyList<HarnessVariable> templates, IReadOnlyList<string> outputs,
        string packageName, PackageBody pkg)
    {
        if (_contexts.ContainsKey(body)) return;
        var subRoutines = new List<string>(pkg.Members.Count);
        foreach (var m in pkg.Members)
        {
            // A package member's verbatim source is "PROCEDURE/FUNCTION …" (no DECLARE); a harness sub-routine
            // needs the DECLARE keyword.
            subRoutines.Add("DECLARE " + pkg.Source.Substring(m.Start, m.Length));
        }
        subRoutines.AddRange(PsqlDeclarationExtractor.Extract(body, source).SubRoutines); // the member's own locals
        _contexts[body] = new RoutineContext(
            source, model, templates, new HashSet<string>(outputs, StringComparer.OrdinalIgnoreCase),
            subRoutines, Trigger: null, PackageName: packageName, PackageMembers: pkg.Members);
    }

    /// <inheritdoc/>
    /// <remarks>D9 seam c (§6.4): resolves a lone <b>local-function</b> call for step-into. Walks the lexical
    /// scope chain for a local <c>DECLARE FUNCTION</c> named <paramref name="call"/>.Name (nearest scope first,
    /// so an inner declaration shadows a like-named outer — exactly how name resolution works, spec §6), builds
    /// its frame from the <b>already-parsed AST body</b> (no server source fetch — a local routine lives in the
    /// enclosing routine's AST), seeds its input parameters from the call's arguments through the SAME seeding
    /// harness a procedure step-into uses (Contract #4), sets its <see cref="Frame.LexicalParent"/> per the §6.3
    /// version gate (FB5 = the declaring frame, a true closure; FB3/FB4 = null, a closed scope), and carries its
    /// <c>RETURNS</c> base type (R2) for the Expression Harness. Returns null when <paramref name="call"/> is not
    /// an in-scope local function (a stored / built-in / package function) — the caller then runs the whole
    /// expression on the server = a 100%-faithful step-over (§5.3/§6.4).</remarks>
    public DebugRoutine? ResolveFunction(CallExpression call, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(frame);
        if (string.IsNullOrWhiteSpace(call.Name)) return null;

        if (TryFindLocalRoutine(call.Name!, frame, SubroutineKind.Function) is not { } local) return null;
        try
        {
            return Await(BuildLocalFunctionAsync(
                call, frame, local.Declaration, local.DeclaringFrame, CancellationToken.None));
        }
        catch (FbException)
        {
            return null; // param / return base-type derivation unreadable → step over in place (§5.3), never guess
        }
    }

    /// <inheritdoc/>
    /// <remarks>D9 seam c (§6.4): evaluates a function frame's <c>RETURN &lt;expr&gt;</c> operand through the
    /// <b>Expression Harness</b> typed as the frame's <see cref="Frame.ReturnType"/> (R2) — the SAME mechanism as
    /// <see cref="EvaluateCondition"/> (they share <see cref="EvaluateExpression"/>), so the server computes the
    /// value with full fidelity (types, coercion, <c>NULL</c>). Never routes a bare <c>RETURN</c> through the
    /// Statement Harness — <c>RETURN</c> is invalid inside an <c>EXECUTE BLOCK</c>.</remarks>
    public ReturnOutcome EvaluateReturn(IExecutableStatement returnStatement, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(returnStatement);
        ArgumentNullException.ThrowIfNull(frame);

        var ctx = Ctx(frame);
        var node = AsNode(returnStatement);
        // A function frame always carries its RETURNS base type; fall back to the wide text column only defensively
        // (an unknown type is cast to text — the honest general choice, never a guessed typed value, §F).
        string resultType = frame.ReturnType ?? EvaluationResultType;
        var (reads, _) = ResolveReadWrite(ctx, node, frame); // the RETURN operand's read set (+ seam-b fixpoint)
        var (value, error) = EvaluateExpression(ctx, frame, ReturnOperandExpression(ctx.Source, node), resultType, reads);
        return error is not null ? ReturnOutcome.Raised(error) : ReturnOutcome.Of(value);
    }

    // Finds a LOCAL sub-routine of <paramref name="kind"/> named <paramref name="name"/> visible from
    // <paramref name="frame"/>, walking the lexical scope chain (this frame's routine body, then its declaring
    // frame's, …) exactly as name resolution does (spec §6) — so nearest scope first: an inner declaration
    // SHADOWS a like-named outer one. Returns the declaration + the frame that declares it (the callee's lexical
    // parent). Only a routine with a real Body qualifies (a forward declaration — null body — is not runnable;
    // the real definition carries the body). Shared by ResolveRoutine (Procedure — EXECUTE PROCEDURE step point)
    // and ResolveFunction (Function — a lone call in a value-consuming position).
    private static (SubroutineDeclaration Declaration, Frame DeclaringFrame)? TryFindLocalRoutine(
        string name, Frame frame, SubroutineKind kind)
    {
        for (var f = frame; f is not null; f = f.LexicalParent)
        {
            foreach (var r in f.Body.LocalRoutines)
            {
                if (r.Kind == kind && r.Body is not null
                    && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return (r, f);
                }
            }
        }
        return null;
    }

    // Builds a stepped-into local FUNCTION's frame (D9 seam c) — mirrors BuildLocalRoutineAsync: templates + the
    // RETURNS base type from the AST header (a local routine is not a catalog object), input params seeded from
    // the call's arguments through the shared seeding harness, LexicalParent per the §6.3 version gate, source +
    // model the ENCLOSING routine's (a local routine's spans + scope live there). Carries ReturnType so the
    // interpreter's EvaluateReturn types the RETURN result column.
    private async Task<DebugRoutine?> BuildLocalFunctionAsync(
        CallExpression call, Frame callerFrame, SubroutineDeclaration routine, Frame declaringFrame,
        CancellationToken cancellationToken)
    {
        var body = routine.Body!;
        var callerCtx = Ctx(callerFrame);

        var layout = await FirebirdDebugMetadata
            .BuildLocalRoutineFrameVariablesAsync(_session, routine, callerCtx.Source, cancellationToken)
            .ConfigureAwait(false);

        // Seed the callee's input params by evaluating the call's arguments in the CALLER frame (the SAME harness
        // as a procedure step-into, Contract #4). The caller's own body tokens cover the argument spans — enough
        // for the colon→bare rewrite, which is span-scoped.
        var initialValues = await SeedInputParametersAsync(
            call.Arguments, callerFrame.Body.Tokens, call.Start, callerFrame, layout.InputParameters, cancellationToken)
            .ConfigureAwait(false);

        Register(body, callerCtx.Source, callerCtx.Model, layout.Variables, layout.OutputParameters);

        int serverMajor = FirebirdDdlReader.ParseServerMajor(_session.Connection.ServerVersion);
        Frame? lexicalParent = serverMajor >= 5 ? declaringFrame : null; // §6.3 gate (FB5 closure / FB3+FB4 closed)

        return new DebugRoutine(
            routine.Name ?? "(local function)", body, initialValues, layout.OutputParameters,
            lexicalParent: lexicalParent, source: callerCtx.Source, model: callerCtx.Model, returnType: layout.ReturnType);
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
            exec.Arguments, exec.Tokens, exec.Start, callerFrame, layout.InputParameters, cancellationToken).ConfigureAwait(false);

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
            exec.Arguments, exec.Tokens, exec.Start, callerFrame, layout.InputParameters, cancellationToken).ConfigureAwait(false);

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
        IReadOnlyList<CallArgument> arguments, IReadOnlyList<SqlToken> callTokens, int callStart,
        Frame callerFrame, IReadOnlyList<HarnessVariable> inputParameters, CancellationToken cancellationToken)
    {
        int n = Math.Min(arguments.Count, inputParameters.Count);
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
                callerCtx.Source, callTokens, arguments[i].Start, arguments[i].Length);
            fragment.Append(argVar).Append(" = ").Append(argText.Trim()).Append(';');
        }

        var reads = ReadWriteSetAnalyzer.InScopeLocals(callerCtx.Model, callStart);
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
        var (s, e) = ConditionBounds(node);
        if (s >= 0 && e <= source.Length && e > s)
        {
            return source.Substring(s, e - s);
        }
        // Fallback (malformed / unexpected shape): the whole node text. §F: correctness over cleverness —
        // Firebird will report a syntax error rather than the debugger silently guessing a boolean.
        return Slice(source, node);
    }

    // The source bounds (start, exclusive end) of the parenthesised IF/WHILE condition — the first top-level
    // (…) group after the keyword — or (-1, -1) when the shape is not recognised. Split from ConditionExpression
    // so D10 can substitute NEW/OLD over the same region (not only slice it).
    private static (int Start, int End) ConditionBounds(SqlNode node)
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
                        return (tokens[open].Start, tokens[i].End);
                    }
                }
            }
        }
        return (-1, -1);
    }

    private static TextSpan SpanOf(SqlNode node) => new(node.Start, node.Length);

    // The regions of a statement inside which a NEW/OLD context reference must be COLON-prefixed for Firebird
    // (gotchas #247/#248): the whole node when it IS a DSQL statement (a reused SELECT…INTO / INSERT / UPDATE /
    // DELETE / MERGE — every unqualified name there is column-scoped), otherwise each embedded scalar/EXISTS
    // subquery inside a PSQL statement (a subquery in an assignment RHS, or in an IF/WHILE condition). A pure
    // PSQL statement with no embedded subquery yields none ⇒ every reference stays bare. AST-driven (the parser
    // models embedded subqueries as SubqueryExpression, Etap 6.9 / B3), never a token scan (Contract #1).
    private static IReadOnlyList<TextSpan> ColonRegions(SqlNode node)
    {
        if (node is not PsqlStatement)
        {
            return new[] { SpanOf(node) };
        }

        List<TextSpan>? regions = null;
        foreach (var descendant in node.DescendantNodes())
        {
            if (descendant is SubqueryExpression)
            {
                (regions ??= new List<TextSpan>()).Add(SpanOf(descendant));
            }
        }
        return regions ?? (IReadOnlyList<TextSpan>)System.Array.Empty<TextSpan>();
    }

    // Distinct union preserving first-seen order (a ∪ b) — used to fold a trigger's context reads/writes into the
    // local read/write set. Returns the input unchanged when one side is empty (the common non-trigger case).
    private static IReadOnlyList<string> Union(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (b.Count == 0) return a;
        if (a.Count == 0) return b;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(a.Count + b.Count);
        foreach (var x in a) if (seen.Add(x)) result.Add(x);
        foreach (var x in b) if (seen.Add(x)) result.Add(x);
        return result;
    }

    // True when a cursor query references a NEW/OLD record alias (a RecordAlias reference within the query span).
    // Reference-driven, so a 'NEW.' inside a string literal in the query never trips it (D10 §F boundary guard).
    private static bool QueryReferencesContext(SemanticModel model, SqlNode query)
    {
        foreach (var r in model.References)
        {
            if (r.Role == ReferenceRole.RecordAlias
                && r.Span.Start >= query.Start && r.Span.End <= query.End)
            {
                return true;
            }
        }
        return false;
    }

    // The operand of a RETURN leaf (D9 seam c) — everything after the RETURN keyword, up to the trailing ';'
    // — a valid scalar expression for the Expression-mode harness. Read from the node's own tokens (never
    // re-parsed) and sliced verbatim from source; a bare RETURN with no operand falls back to the node text
    // (the server then reports it, never a guess — §F).
    private static string ReturnOperandExpression(string source, SqlNode node)
    {
        if (node is PsqlStatement psql)
        {
            var toks = psql.Tokens;
            int lo = 1; // skip the leading RETURN keyword
            int hi = toks.Count;
            while (hi > lo && toks[hi - 1].Kind == TokenKind.Semicolon) hi--; // drop the terminator
            if (hi > lo)
            {
                int s = toks[lo].Start, e = toks[hi - 1].End;
                if (s >= 0 && e <= source.Length && e > s)
                {
                    return source.Substring(s, e - s);
                }
            }
        }
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
