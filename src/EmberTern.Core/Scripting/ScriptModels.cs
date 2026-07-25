using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Scripting;

/// <summary>
/// High-level category of a single script statement, mapped in the Firebird layer from
/// the driver's <c>SqlStatementType</c>. Drives the results-grid "Type" column, the
/// row-bearing-vs-non-row-bearing execution branch, and the disallowed-statement check
/// (<see cref="ScriptValidation"/>): a statement the Script Executor runs in ONE
/// caller-controlled transaction on an established connection must not carry its own
/// transaction control or session directives.
/// </summary>
public enum ScriptStatementKind
{
    /// <summary>CREATE / ALTER / DROP / RECREATE / GRANT / REVOKE / COMMENT ON /
    /// SET GENERATOR / SET STATISTICS — participates in the transaction (Firebird DDL is transactional).</summary>
    Ddl,
    /// <summary>INSERT / UPDATE / DELETE / MERGE — reports RecordsAffected.</summary>
    Dml,
    /// <summary>SELECT — row-bearing; the executor counts rows (capped) rather than materializing them.</summary>
    Select,
    /// <summary>EXECUTE PROCEDURE — may return a single row of output parameters.</summary>
    ExecuteProcedure,
    /// <summary>EXECUTE BLOCK — may be row-bearing.</summary>
    ExecuteBlock,
    /// <summary>COMMIT / ROLLBACK / SET TRANSACTION — DISALLOWED (the executor owns the transaction).</summary>
    TransactionControl,
    /// <summary>CONNECT / CREATE DATABASE / SET NAMES / SET SQL DIALECT / … — DISALLOWED
    /// (connection/session directives that can't be honoured on an established managed connection).</summary>
    SessionControl,
    /// <summary>Anything the mapper doesn't recognise — executed as a non-row statement.</summary>
    Unknown,
}

/// <summary>
/// One executable statement extracted from a script by <c>FirebirdScriptParser</c> (which
/// wraps the driver's <c>FbScript.Parse()</c> — SET TERM / PSQL-body / literal / comment
/// aware). <see cref="Text"/> is the statement WITHOUT its terminator. <see cref="SourceOffset"/>
/// is a best-effort character offset into the original script (for "click a result → jump to
/// the statement" navigation); -1 when it could not be located.
/// </summary>
public sealed record ScriptStatement(string Text, ScriptStatementKind Kind, int SourceOffset, int SourceLength)
{
    /// <summary>True when the statement was located in the source (offset ≥ 0).</summary>
    public bool HasSourceRange => SourceOffset >= 0;
}

/// <summary>Outcome of running a single script statement — one row in the results grid.</summary>
public sealed record ScriptStatementResult(
    int Index,
    string Text,
    ScriptStatementKind Kind,
    bool Success,
    int? RecordsAffected,
    int? RowCount,
    TimeSpan Elapsed,
    string? Error);

/// <summary>How the Script Executor finalizes its single transaction.</summary>
public enum ScriptTransactionMode
{
    /// <summary>DEFAULT — run every statement, leave the transaction OPEN, let the user
    /// review the results and then Commit or Rollback (the house style, hard rule #3).</summary>
    Manual,
    /// <summary>Run every statement, then COMMIT if none failed else ROLLBACK — never a
    /// half-applied script, never per-statement autocommit.</summary>
    AutoCommitOnSuccess,

    /// <summary>DEPLOYMENT — run statements in order on ONE lane, committing at a boundary after
    /// each schema (DDL/DCL) statement so a later statement can use an object an earlier one
    /// created (gotcha #213 — a transaction cannot use an object it created but has not committed).
    /// Schema segments run WAIT-bounded (Developer-Mode-aware); data segments stay NOWAIT. This is
    /// the only shape Firebird permits for a MIXED DDL+DML migration (what isql's
    /// <c>SET AUTODDL ON</c> does). <b>NOT atomic</b> — a segment that already committed stays
    /// applied if a later segment fails; the trade-off (surfaced, not hidden) is the whole point,
    /// since Firebird cannot both let a transaction use an object it created and keep it
    /// rollbackable. See <see cref="ScriptSegmentPlanner"/> and the transaction review §5.</summary>
    Sequenced,
}

/// <summary>Aggregate outcome of a script run.</summary>
public sealed record ScriptRunOutcome(
    IReadOnlyList<ScriptStatementResult> Results,
    bool TransactionLeftOpen,
    bool AnyFailed,
    bool Cancelled)
{
    public int SuccessCount => Results.Count(r => r.Success);
    public int FailedCount => Results.Count(r => !r.Success);
}
