using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

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
/// (seam b) matches these fields against a <see cref="WhenCondition"/>; D1 only carries them.</summary>
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
    public DebugRoutine(string name, BlockStatement body, IReadOnlyDictionary<string, object?>? initialValues = null)
    {
        Name = name;
        Body = body;
        InitialValues = initialValues;
    }

    /// <summary>The callee's name (for the call stack / breadcrumbs).</summary>
    public string Name { get; }

    /// <summary>The callee's body — a <see cref="BlockStatement"/> the interpreter runs as a new frame.</summary>
    public BlockStatement Body { get; }

    /// <summary>The argument values bound into the new frame's scope, or null.</summary>
    public IReadOnlyDictionary<string, object?>? InitialValues { get; }
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

    /// <summary>Opens the cursor of a <c>FOR SELECT</c> loop against <paramref name="frame"/>'s values.</summary>
    IDebugCursor OpenCursor(ForSelectStatement loop, Frame frame);

    /// <summary>Resolves a call step point (e.g. <c>EXECUTE PROCEDURE</c>) to a callee body for step-into,
    /// or null when it is not a resolvable call (then the caller executes it in place instead).</summary>
    DebugRoutine? ResolveRoutine(IExecutableStatement call, Frame frame);

    /// <summary>Sets a SAVEPOINT on entry to a simulated frame (spec §4.5 — call atomicity is reconstructed
    /// one savepoint per frame). Named by the frame's <see cref="Frame.SavepointName"/>.</summary>
    void EnterFrameSavepoint(string name);

    /// <summary>Releases a frame's savepoint on its NORMAL exit. (The unhandled-exit <c>ROLLBACK TO</c> is
    /// seam b, driven by the <see cref="ExceptionRouter"/>.)</summary>
    void LeaveFrameSavepoint(string name);
}
