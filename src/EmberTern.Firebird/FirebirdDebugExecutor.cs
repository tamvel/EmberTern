using System;
using System.Collections.Generic;
using System.Data;
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
/// <b>Boundaries (§F — explained, not guessed):</b> stepping <em>into</em> a routine
/// (<see cref="ResolveRoutine"/>) is not yet resolved, so a call runs on the server in place — which is
/// <em>step-over</em> and 100% faithful (§5.3); nested stored routines are D8, local routines D9. A
/// <c>FOR SELECT</c> cursor (<see cref="OpenCursor"/>) is stepped through the Cursor Bridge (D6, §7); a
/// <c>FOR EXECUTE STATEMENT</c> dynamic cursor is refused with a clear message.
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

    private readonly DebugSessionConnection _session;
    private readonly string _source;
    private readonly SemanticModel _model;
    private readonly IReadOnlyList<HarnessVariable> _variableTemplates;
    private readonly HashSet<string> _outputParameters;

    private FirebirdDebugExecutor(
        DebugSessionConnection session,
        string source,
        SemanticModel model,
        IReadOnlyList<HarnessVariable> variableTemplates,
        IReadOnlyList<string> outputParameters)
    {
        _session = session;
        _source = source;
        _model = model;
        _variableTemplates = variableTemplates;
        _outputParameters = new HashSet<string>(outputParameters, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Creates the executor for a standalone routine: resolves its frame variable templates (verbatim
    /// declarations R3 + base types R2) from metadata once, then binds them to the session. <paramref name="source"/>
    /// is the routine's full source (the span backing for fragments + declarations), <paramref name="body"/>
    /// its parsed body, <paramref name="model"/> its semantic model (the read/write sets consume it).</summary>
    public static async Task<FirebirdDebugExecutor> CreateAsync(
        DebugSessionConnection session,
        string? routineName,
        string source,
        BlockStatement body,
        SemanticModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);

        var layout = await FirebirdDebugMetadata
            .BuildFrameVariablesAsync(session, routineName, body, source, cancellationToken)
            .ConfigureAwait(false);
        return new FirebirdDebugExecutor(session, source, model, layout.Variables, layout.OutputParameters);
    }

    // ── IDebugExecutor ──────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public StatementOutcome ExecuteStatement(IExecutableStatement statement, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(frame);

        // SUSPEND is control flow, not server semantics: it yields the current output-parameter values as the
        // routine's output row. Emit it client-side (no harness, no round-trip) — §3.1 (client owns control).
        if (statement is PsqlLeafStatement { Kind: PsqlLeafKind.Suspend })
        {
            return StatementOutcome.Suspended(SnapshotOutputs(frame));
        }

        var node = AsNode(statement);
        var (reads, writes) = ResolveReadWrite(node);
        var request = new HarnessRequest
        {
            Fragment = Slice(node),
            Mode = HarnessMode.Statement,
            Variables = BindValues(frame),
            Reads = reads,
            Writes = writes,
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

        var node = AsNode(owner);
        var (reads, _) = ResolveReadWrite(node);
        var request = new HarnessRequest
        {
            Fragment = ConditionExpression(node),
            Mode = HarnessMode.Expression,
            ExpressionResultType = BooleanResultType,
            Variables = BindValues(frame),
            Reads = reads,
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

        var names = ReadWriteSetAnalyzer.InScopeLocals(_model, request.ScopeOffset);
        if (names.Count == 0)
        {
            // At/before an offset with no locals in scope — inject every known frame variable (still §3.5).
            names = AllTemplateNames();
        }

        bool expression = request.Kind == EvaluationKind.Expression;
        var harness = HarnessBuilder.Build(new HarnessRequest
        {
            Fragment = request.Fragment,
            Mode = expression ? HarnessMode.Expression : HarnessMode.Statement,
            ExpressionResultType = expression ? EvaluationResultType : null,
            Variables = BindValues(frame),
            Reads = names,
            Writes = expression ? Array.Empty<string>() : names, // an expression writes nothing; a statement may
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

    private IReadOnlyList<string> AllTemplateNames()
    {
        var names = new List<string>(_variableTemplates.Count);
        foreach (var t in _variableTemplates) names.Add(t.Name);
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

        var plan = CursorBridge.Build(_source, loop);
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
    /// <remarks>D2: a call is not resolved for step-into, so the interpreter runs it in place on the server —
    /// which is step-over (100% faithful, §5.3). Step-into a stored routine is D8, a local routine D9.</remarks>
    public DebugRoutine? ResolveRoutine(IExecutableStatement call, Frame frame) => null;

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
    private (IReadOnlyList<string> Reads, IReadOnlyList<string> Writes) ResolveReadWrite(SqlNode node)
    {
        var rw = ReadWriteSetAnalyzer.Analyze(node, _model);
        if (rw.Reads.Count == 0 && rw.Writes.Count == 0)
        {
            var all = ReadWriteSetAnalyzer.InScopeLocals(_model, node.Start);
            return (all, all);
        }
        return (rw.Reads, rw.Writes);
    }

    // ── Frame → harness variables ───────────────────────────────────────────────────────────────────

    // Every in-scope variable is declared in the harness (verbatim, R3); its current value (if any) rides
    // along so HarnessBuilder can inject the reads (R1 skips a null/absent value). Over-declaring a variable
    // the fragment does not use is harmless — the read/write set narrows what is injected/returned (§3.5).
    private IReadOnlyList<HarnessVariable> BindValues(Frame frame)
    {
        var bound = new List<HarnessVariable>(_variableTemplates.Count);
        foreach (var template in _variableTemplates)
        {
            if (frame.TryResolveValue(template.Name, out var value))
            {
                bound.Add(template with { Value = value, HasValue = true });
            }
            else
            {
                bound.Add(template);
            }
        }
        return bound;
    }

    private IReadOnlyDictionary<string, object?> SnapshotOutputs(Frame frame)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _outputParameters)
        {
            row[name] = frame.TryResolveValue(name, out var v) ? v : null;
        }
        return row;
    }

    // ── Source spans ────────────────────────────────────────────────────────────────────────────────

    private string Slice(SqlNode node)
    {
        int start = Math.Clamp(node.Start, 0, _source.Length);
        int length = Math.Clamp(node.Length, 0, _source.Length - start);
        return _source.Substring(start, length);
    }

    // The parenthesised condition of an IF/WHILE header — the first top-level (…) group after the keyword.
    // Firebird requires the condition in parens (IF (<cond>) THEN / WHILE (<cond>) DO), so the first '(' opens
    // it and its match closes it. Read from the node's tokens (never re-parsed) and sliced verbatim from
    // source; the whole "(<cond>)" is a valid boolean expression for the Expression-mode harness.
    private string ConditionExpression(SqlNode node)
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
                        if (s >= 0 && e <= _source.Length && e > s)
                        {
                            return _source.Substring(s, e - s);
                        }
                        break;
                    }
                }
            }
        }
        // Fallback (malformed / unexpected shape): the whole node text. §F: correctness over cleverness —
        // Firebird will report a syntax error rather than the debugger silently guessing a boolean.
        return Slice(node);
    }

    private static SqlNode AsNode(IExecutableStatement statement)
        => statement as SqlNode
           ?? throw new InvalidOperationException(
               $"Debug: step point {statement.GetType().Name} is not a source node.");

    // ── Sync-over-async bridge (see the class remark) ───────────────────────────────────────────────

    private static void Await(Task task) => task.GetAwaiter().GetResult();

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
