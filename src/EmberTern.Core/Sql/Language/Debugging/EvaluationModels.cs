using System.Collections.Generic;

namespace EmberTern.Core.Sql.Debugging;

// Stage X — Firebird Debugger, milestone D5 (expression evaluation surface, spec §9.5). The model for
// evaluating a USER-SUPPLIED fragment against the current frame — the shared substrate of the three
// surfaces Evaluate / Watches / Immediate (decision 6: one engine, three surfaces). It is deliberately the
// SAME mechanism as a step (§3.2/§3.3): the fragment becomes a generated EXECUTE BLOCK harness via
// HarnessBuilder, run in the debug transaction, and the server computes everything. The only difference
// from a step is that the fragment is arbitrary text (no AST node), so the read/write set cannot be derived
// precisely — the §3.5 "inject all in-scope" primitive (ReadWriteSetAnalyzer.InScopeLocals) is used, which
// is exactly why that primitive was carved out named in D2 (§3.5 remark: "a Watch on an arbitrary
// expression the model did not bind — D5").

/// <summary>Whether a user fragment is evaluated as an <see cref="Expression"/> (a value — Evaluate / a
/// Watch) or run as a <see cref="Statement"/> against the live frame (the Immediate window). An expression
/// produces a value and writes nothing; a statement may assign frame variables (its write-back is applied to
/// the current frame — the Immediate window operates "against the live frame", spec §9.5).</summary>
public enum EvaluationKind
{
    /// <summary>Evaluate to a value (Evaluate / a Watch). Writes nothing to the frame.</summary>
    Expression,

    /// <summary>Run as a statement against the live frame (the Immediate window). May assign frame variables.</summary>
    Statement,
}

/// <summary>The request to evaluate one user fragment (spec §9.5). Carries the verbatim
/// <see cref="Fragment"/>, its <see cref="Kind"/>, and the <see cref="ScopeOffset"/> at which to resolve the
/// in-scope locals to inject (the current step point's offset — §3.5 "inject all in-scope", since an
/// arbitrary fragment has no AST node whose reads/writes could be computed precisely).</summary>
public sealed record EvaluationRequest(string Fragment, EvaluationKind Kind, int ScopeOffset);

/// <summary>
/// The outcome of evaluating a user fragment (spec §9.5). <see cref="Sql"/> is the generated harness — the
/// audit anchor that makes §F checkable (§10.3 Executed SQL: "what did EmberTern actually send?"). On
/// success, <see cref="Value"/> is the evaluated value (Expression mode; null in Statement mode) and
/// <see cref="Writes"/> is the frame write-back (Statement mode — applied to the live frame). On failure,
/// <see cref="Error"/> carries the raised exception (mapped from the driver, never parsed from a message).
/// </summary>
public sealed record EvaluationResult(
    string Sql,
    bool Success,
    object? Value,
    DebugError? Error,
    IReadOnlyDictionary<string, object?>? Writes)
{
    /// <summary>True when the fragment raised (its identity is in <see cref="Error"/>).</summary>
    public bool HasError => Error is not null;

    /// <summary>True when the fragment wrote at least one frame variable — a side effect on the frame
    /// (a statement that assigned something). DB side effects (real SQL in the debug transaction) are a
    /// separate, always-present possibility; this reports only the frame write-back.</summary>
    public bool HadWriteBack => Writes is { Count: > 0 };

    /// <summary>A successful evaluation: the value (Expression) and/or the frame write-back (Statement).</summary>
    public static EvaluationResult Ok(string sql, object? value, IReadOnlyDictionary<string, object?>? writes)
        => new(sql, true, value, null, writes);

    /// <summary>A raised evaluation: the fragment threw on the server.</summary>
    public static EvaluationResult Failed(string sql, DebugError error)
        => new(sql, false, null, error, null);
}
