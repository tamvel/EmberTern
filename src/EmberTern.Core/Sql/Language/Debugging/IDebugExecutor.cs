using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Debugging;

// The one seam between the interpreter (control flow — client-owned) and the server (semantics —
// server-owned). Every server interaction goes through this contract; there is no second path (spec §3.3).
// It is the single precedented exception to Architecture rule #2 ("no interfaces without two concrete
// implementations"): Core cannot reference EmberTern.Firebird, so it declares the contract it needs —
// exactly as Core/Sql/Language/Semantics does with ISqlMetadataProvider. D2/D6/D8 implement it for real
// (the EXECUTE BLOCK harness, the cursor bridge, routine source fetch); D1 drives it with a scripted fake.
//
// Nothing here re-implements Firebird semantics: the interpreter hands the executor an AST step point plus
// the current frame (for the read set) and receives an outcome. It never evaluates an expression, coerces
// a type, or decides a boolean itself.

/// <summary>An exception's identity, as the driver reports it (spec §3.6: mapping comes from
/// <c>FbException</c>'s SQLSTATE / GDS codes, never from parsing messages). The <see cref="ExceptionRouter"/>
/// matches these fields against a <see cref="WhenCondition"/>.</summary>
public sealed record DebugError(
    string? ExceptionName = null,
    long? GdsCode = null,
    string? GdsCodeSymbol = null,
    int? SqlCode = null,
    string? SqlState = null,
    string? Message = null);

/// <summary>The result of executing one leaf/DML statement: its status, the variables it wrote (applied to
/// the frame — the read/write-set-driven write-back of spec §3.5), and the error when it raised.</summary>
public sealed record StatementOutcome(
    ExecutionStatus Status,
    IReadOnlyDictionary<string, object?>? Writes = null,
    DebugError? Error = null)
{
    public static StatementOutcome Normal(IReadOnlyDictionary<string, object?>? writes = null)
        => new(ExecutionStatus.Normal, writes);

    public static StatementOutcome Suspended(IReadOnlyDictionary<string, object?>? writes = null)
        => new(ExecutionStatus.Suspended, writes);

    public static StatementOutcome Raised(DebugError error)
        => new(ExecutionStatus.Raised, null, error);
}

/// <summary>The result of evaluating a boolean control-flow condition (an <c>IF</c>/<c>WHILE</c> header):
/// the boolean the server computed, or an error when the condition itself raised.</summary>
public sealed record ConditionOutcome(bool? Value, DebugError? Error = null)
{
    public static ConditionOutcome True { get; } = new(true);
    public static ConditionOutcome False { get; } = new(false);
    public static ConditionOutcome Of(bool value) => value ? True : False;
    public static ConditionOutcome Raised(DebugError error) => new(null, error);
}

/// <summary>A live <c>FOR SELECT</c> cursor. Fetched one row at a time, in the real debug transaction
/// (the Cursor Bridge, D6); in D1 a fake scripts the rows. Each fetch yields the <c>INTO</c>-target writes
/// to apply into the frame, or null at end of cursor.</summary>
public interface IDebugCursor
{
    /// <summary>The next row's <c>INTO</c>-target variable writes, or null when the cursor is exhausted.</summary>
    IReadOnlyDictionary<string, object?>? FetchNext();

    /// <summary>Closes the cursor.</summary>
    void Close();
}

/// <summary>A callee resolved for step-into: the routine's body (an AST the interpreter will interpret as
/// a new frame) plus the argument values bound into that frame. Fetching + parsing a stored routine's
/// source is D8; a local routine resolves from the AST (D9); D1 drives it from a fake.</summary>
public sealed class DebugRoutine
{
    public DebugRoutine(
        string name,
        BlockStatement body,
        IReadOnlyDictionary<string, object?>? initialValues = null,
        IReadOnlyList<string>? outputParameterNames = null,
        Frame? lexicalParent = null,
        string? source = null,
        SemanticModel? model = null,
        string? returnType = null)
    {
        Name = name;
        Body = body;
        InitialValues = initialValues;
        OutputParameterNames = outputParameterNames ?? System.Array.Empty<string>();
        LexicalParent = lexicalParent;
        Source = source;
        Model = model;
        ReturnType = returnType;
    }

    /// <summary>The callee's name (for the call stack / breadcrumbs).</summary>
    public string Name { get; }

    /// <summary>The callee's body — a <see cref="BlockStatement"/> the interpreter runs as a new frame.</summary>
    public BlockStatement Body { get; }

    /// <summary>The callee's full source text (for the call-stack UI to show its routine + compute lines);
    /// null when unavailable. Flows onto the pushed <see cref="Frame.Source"/>.</summary>
    public string? Source { get; }

    /// <summary>The callee's semantic model (the roster the Variables panel projects when the call stack selects
    /// this frame — spec §5.2); null when unavailable. Flows onto the pushed <see cref="Frame.Model"/>, on the
    /// same offsets as <see cref="Source"/>.</summary>
    public SemanticModel? Model { get; }

    /// <summary>The argument values bound into the new frame's scope (the callee's input parameters, seeded
    /// from the call's evaluated arguments), or null.</summary>
    public IReadOnlyDictionary<string, object?>? InitialValues { get; }

    /// <summary>The callee's <b>output</b> parameter names, in declaration order (empty when it has none) —
    /// on the callee frame's normal return, its outputs are written positionally into the caller's
    /// <c>RETURNING_VALUES</c> targets (spec §5).</summary>
    public IReadOnlyList<string> OutputParameterNames { get; }

    /// <summary>The callee's <b>lexical</b> parent frame for the scope chain (<see cref="Frame.LexicalParent"/>):
    /// <b>null</b> for a stored routine (a closed scope — D8), the <b>declaring</b> frame for a local
    /// sub-routine (a closure over the parent — D9). The call-stack parent is always the caller and is set by
    /// the interpreter, independently of this.</summary>
    public Frame? LexicalParent { get; }

    /// <summary>A stepped-into local <b>function</b>'s <c>RETURNS</c> base type (R2) — the type the Expression
    /// Harness gives the result column that computes the frame's <c>RETURN</c> value (Stage X / D9 seam c,
    /// §6.4). Null for a procedure callee (which returns via <see cref="OutputParameterNames"/>). Flows onto
    /// the pushed <see cref="Frame.ReturnType"/>.</summary>
    public string? ReturnType { get; }
}

/// <summary>The result of evaluating a function's <c>RETURN &lt;expr&gt;</c> operand (spec §6.4, D9 seam c):
/// the value the server computed via the Expression Harness, or an error when the operand itself raised.</summary>
public sealed record ReturnOutcome(object? Value, DebugError? Error = null)
{
    public static ReturnOutcome Of(object? value) => new(value);
    public static ReturnOutcome Raised(DebugError error) => new(null, error);
}

/// <summary>The server seam. The interpreter calls it; it never drives the interpreter.</summary>
public interface IDebugExecutor
{
    /// <summary>Runs one leaf / DML step point with <paramref name="frame"/>'s current values as the read
    /// set, returning the outcome (normal + writes, suspended + row, or raised).</summary>
    StatementOutcome ExecuteStatement(IExecutableStatement statement, Frame frame);

    /// <summary>Evaluates the boolean condition of a control-flow header (<paramref name="owner"/> is the
    /// <c>IF</c>/<c>WHILE</c> node) against <paramref name="frame"/>'s values.</summary>
    ConditionOutcome EvaluateCondition(IExecutableStatement owner, Frame frame);

    /// <summary>Evaluates a <b>user-supplied</b> boolean expression — a breakpoint condition (D12, spec §9.8.2)
    /// — against <paramref name="frame"/>, resolving the in-scope locals to inject at
    /// <paramref name="scopeOffset"/> (the breakpoint's step-point offset, §3.5). It is the SAME typed-boolean
    /// server path as the <c>IF</c>/<c>WHILE</c> overload above (a <c>BOOLEAN</c> Expression Harness), fed a
    /// string fragment instead of an AST node — <b>not a second evaluator</b> (the plan's D12 constraint). NULL
    /// / false → the breakpoint does not break; an error is surfaced (never silently skipped, §F).</summary>
    ConditionOutcome EvaluateCondition(string fragment, int scopeOffset, Frame frame);

    /// <summary>Evaluates a <b>user-supplied fragment</b> against <paramref name="frame"/> (spec §9.5 — the
    /// Evaluate / Watches / Immediate surfaces). It is the SAME harness mechanism as a step: the fragment
    /// becomes a generated <c>EXECUTE BLOCK</c>, run in the debug transaction, and the server computes
    /// everything. The fragment has no AST node, so the injected read/write set is the §3.5 "inject all
    /// in-scope" primitive (<see cref="ReadWriteSetAnalyzer.InScopeLocals"/>). The returned
    /// <see cref="EvaluationResult"/> carries the value (Expression), the frame write-back (Statement) and
    /// the generated SQL (the Executed-SQL audit, §10.3). There is no second evaluator (D5 risk #1).</summary>
    EvaluationResult Evaluate(EvaluationRequest request, Frame frame);

    /// <summary>Opens the cursor of a <c>FOR SELECT</c> loop against <paramref name="frame"/>'s values.</summary>
    IDebugCursor OpenCursor(ForSelectStatement loop, Frame frame);

    /// <summary>Resolves a call step point (e.g. <c>EXECUTE PROCEDURE</c>) to a callee body for step-into,
    /// or null when it is not a resolvable call (then the caller executes it in place instead).</summary>
    DebugRoutine? ResolveRoutine(IExecutableStatement call, Frame frame);

    /// <summary>Resolves a lone local-<b>function</b> call (Stage X / D9 seam c, §6.4) to a callee body for
    /// step-into, or null when <paramref name="call"/> is not an in-scope local function (a stored / built-in
    /// / package call ⇒ the caller runs the whole statement server-side = step-over, 100% faithful). Mirrors
    /// <see cref="ResolveRoutine"/>; the returned <see cref="DebugRoutine"/> carries the callee's <c>RETURNS</c>
    /// base type (<see cref="DebugRoutine.ReturnType"/>) for the Expression Harness that computes its
    /// <c>RETURN</c> value, and its lexical parent (the declaring frame — a closure, §6).</summary>
    DebugRoutine? ResolveFunction(CallExpression call, Frame frame);

    /// <summary>Evaluates a function frame's <c>RETURN &lt;expr&gt;</c> operand (Stage X / D9 seam c, §6.4) via
    /// the <b>Expression Harness</b> typed as the frame's <see cref="Frame.ReturnType"/>, returning the computed
    /// value or an error. It reuses the same Expression-Harness mechanism as <see cref="EvaluateCondition"/> —
    /// the server computes the value — and never runs a bare <c>RETURN</c> through the Statement Harness (a
    /// <c>RETURN</c> is invalid inside an <c>EXECUTE BLOCK</c>).</summary>
    ReturnOutcome EvaluateReturn(IExecutableStatement returnStatement, Frame frame);

    /// <summary>Sets a SAVEPOINT on entry to a simulated frame (spec §4.5 — call atomicity is reconstructed
    /// one savepoint per frame). Named by the frame's <see cref="Frame.SavepointName"/>.</summary>
    void EnterFrameSavepoint(string name);

    /// <summary>Releases a frame's savepoint on its NORMAL exit.</summary>
    void LeaveFrameSavepoint(string name);

    /// <summary>Rolls the debug transaction back to a frame's savepoint on its <b>unhandled</b> exit — an
    /// exception is propagating out of this simulated frame, so its side effects are undone before the
    /// caller's handler (or the session fault) observes the database (spec §4.5: a real call is undone
    /// atomically; a simulated frame reconstructs that with one savepoint per frame). Driven by the
    /// <see cref="ExceptionRouter"/>. <b>NOT</b> called when a block's own <c>WHEN</c> handler catches —
    /// there the prior statements must survive; only a frame that fails to catch is rolled back.</summary>
    void RollbackFrameSavepoint(string name);
}
