using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// How a stepped-into local <b>function</b>'s <c>RETURN</c> value is consumed by the caller statement that
/// stepped into it — Stage X / D9 seam c (§6.4). It is a generalisation of the procedure
/// <c>RETURNING_VALUES</c> write-back (<c>DebugSession.ApplyReturningValues</c>): one variant per
/// value-consuming position, run against the caller frame on the function frame's <b>normal</b> return. Pure
/// control-flow data — the return value was already computed by the callee frame server-side; a continuation
/// never evaluates an expression, coerces a type, or touches the server.
/// <para>
/// The <see cref="RecognizeStepInto"/> factory is deliberately the <b>single place</b> that decides whether a
/// step point is a step-into-able local-function position and, if so, which continuation consumes its return
/// — so the interpreter's four value-consuming positions (<c>v = f()</c> / <c>RETURN f()</c> / <c>IF f()</c> /
/// <c>WHILE f()</c>) are handled uniformly instead of scattering near-identical recognition across the
/// IF/WHILE/leaf branches of the step loop. The parser (c1) already recognised the lone call and hung a
/// <see cref="CallExpression"/> off the node; this only routes it to the right continuation.
/// </para>
/// </summary>
internal abstract record FunctionReturnContinuation
{
    private FunctionReturnContinuation() { }

    /// <summary><c>v = f(args)</c> — deliver the return value into the caller's variable
    /// <see cref="Target"/>, then consume the assignment leaf.</summary>
    public sealed record AssignTo(string Target) : FunctionReturnContinuation;

    /// <summary><c>RETURN f(args)</c> — the return value becomes the caller (enclosing function) frame's own
    /// return value; the caller frame then completes and its own continuation fires in turn (recursion
    /// handled naturally by the pop loop).</summary>
    public sealed record SetFrameReturn : FunctionReturnContinuation;

    /// <summary><c>IF (f(args)) THEN …</c> — the return value decides which branch of <see cref="Node"/> the
    /// caller takes.</summary>
    public sealed record BranchIf(IfStatement Node) : FunctionReturnContinuation;

    /// <summary><c>WHILE (f(args)) DO …</c> — the return value decides whether the caller enters
    /// <see cref="Node"/>'s body this iteration.</summary>
    public sealed record DecideWhile(WhileStatement Node) : FunctionReturnContinuation;

    /// <summary>
    /// Maps a step point to a step-into-able local-function call plus the continuation that consumes its
    /// return value at the caller position (§6.4), or null when the step point is not one of the four
    /// value-consuming positions (⇒ the caller runs it normally = step-over). This is the ONE concentration
    /// point for the step-into decision: the interpreter asks it once, so the recognition of "is this a lone
    /// call I can descend into" and the choice of continuation live together, not spread across the step
    /// loop's per-node cases. Whether <see cref="CallExpression.Name"/> is actually an in-scope local
    /// function is the executor's decision (<c>IDebugExecutor.ResolveFunction</c>) — this only classifies the
    /// syntactic position the parser already modelled.
    /// </summary>
    public static FunctionStepInto? RecognizeStepInto(IExecutableStatement step) => step switch
    {
        // An assignment whose whole RHS is a lone call (c1 sets RhsCall + AssignmentTarget together).
        PsqlLeafStatement { RhsCall: { } call, AssignmentTarget: { } target }
            => new FunctionStepInto(call, new AssignTo(target)),
        // A RETURN whose whole operand is a lone call (c1 sets RhsCall with a null AssignmentTarget).
        PsqlLeafStatement { RhsCall: { } call, Kind: PsqlLeafKind.Return }
            => new FunctionStepInto(call, new SetFrameReturn()),
        IfStatement { ConditionCall: { } call } iff
            => new FunctionStepInto(call, new BranchIf(iff)),
        WhileStatement { ConditionCall: { } call } loop
            => new FunctionStepInto(call, new DecideWhile(loop)),
        _ => null,
    };
}

/// <summary>A recognised step-into of a local function (§6.4): the lone <see cref="Call"/> the executor
/// resolves + the <see cref="Continuation"/> that consumes its return value at the caller position.</summary>
internal readonly record struct FunctionStepInto(CallExpression Call, FunctionReturnContinuation Continuation);
