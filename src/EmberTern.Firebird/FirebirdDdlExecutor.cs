using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Executes DDL statements ("CREATE TABLE", "ALTER TABLE", "CREATE GENERATOR",
/// "CREATE TRIGGER", …) on the active <see cref="FirebirdConnectionService"/>.
///
/// DDL statements participate in the user's working transaction when one is
/// active (so a single Compile run can be Rolled Back in one shot), and run
/// without an explicit transaction when none is — Firebird auto-commits each
/// DDL command in that case via the managed driver's implicit per-command tx.
///
/// Multi-statement payloads are split into individual statements (the FB engine
/// does not accept multiple statements in a single <c>FbCommand</c>). The splitter
/// is PSQL-aware: a CREATE/ALTER/RECREATE of a PROCEDURE/TRIGGER/FUNCTION/PACKAGE is
/// kept whole including its DECLARE-section and body semicolons (see
/// <see cref="SplitStatements"/>); plain DDL/DML splits on top-level semicolons.
/// </summary>
public sealed class FirebirdDdlExecutor
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly TransactionService? _transactionService;

    // Krok 1: DDL/Compile executes on the MAIN connection (co-location with the
    // lane that runs Execute Procedure / F5) so a Compile of a just-executed object
    // no longer hits the cross-attachment "object is in use" self-block. The
    // TransactionService (the DATA lane) is consulted only to verify no working
    // transaction is active before we begin our own autonomous DDL tx (gotcha #89).
    public FirebirdDdlExecutor(FirebirdConnectionService connectionService, TransactionService? transactionService = null)
    {
        _connectionService = connectionService;
        _transactionService = transactionService;
    }

    /// <summary>
    /// Runs administrative maintenance statements (e.g. <c>SET STATISTICS INDEX</c>) in
    /// their own short, auto-committed transactions — independent of the working
    /// transaction, so nothing is left pending for the user to Commit (IBExpert-style).
    /// Delegates to <see cref="FirebirdConnectionService.ExecuteAdminBatchAsync"/>;
    /// returns per-statement results (null = ok, otherwise the error message).
    /// </summary>
    public Task<IReadOnlyList<string?>> ExecuteAutonomousBatchAsync(
        IReadOnlyList<string> statements,
        CancellationToken cancellationToken = default,
        IProgress<(int Index, string? Error)>? progress = null)
        // Apply the Developer-Mode-aware DDL TPB (WAIT + lock timeout in Dev Mode, else
        // NOWAIT) so recompile honours the connection's mode instead of the driver default.
        => _connectionService.ExecuteAdminBatchAsync(statements, cancellationToken, progress, BuildDdlTransactionOptions());

    /// <summary>
    /// Splits <paramref name="sql"/> on top-level semicolons, then runs the whole
    /// batch in ONE transaction on the MAIN connection (co-location — see
    /// <see cref="FirebirdConnectionService.ExecuteDdlAsync"/>), auto-committing on
    /// success. The batch is atomic (e.g. ADD FIELD + CREATE GENERATOR + CREATE
    /// TRIGGER all-or-nothing). Uses an explicit NOWAIT TPB — identical to prior
    /// behaviour, but now genuinely explicit (the old autonomous path passed no
    /// FbTransactionOptions, so it silently ignored any configured profile).
    ///
    /// gotcha #89: one FbConnection allows one transaction at a time, so a data
    /// working transaction must be settled first. Surfaces a clear, actionable
    /// message instead of the raw "Parallel transactions are not supported". The
    /// self-block scenario (Execute Procedure → Commit/Rollback → Compile) has the
    /// working tx already settled, so this does not impede it.
    /// Throws <see cref="DdlExecutionException"/> with the server's message on the
    /// first FbException — the caller stops the Compile run at that point.
    /// </summary>
    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;

        var statements = SplitStatements(sql);
        if (statements.Count == 0) return;

        if (_transactionService is { IsActive: true })
        {
            throw new DdlExecutionException(
                "Commit or roll back the active transaction before running DDL.");
        }

        try
        {
            await _connectionService
                .ExecuteDdlAsync(statements, BuildDdlTransactionOptions(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FbException ex)
        {
            throw new DdlExecutionException(ex.Message, ex);
        }
    }

    // Lock timeout (seconds) for Developer Mode's WAIT transactions — bounds the wait
    // so a Compile of a continuously-used object fails with a clear message instead of
    // hanging indefinitely.
    internal const int DdlLockTimeoutSeconds = 10;

    private FbTransactionOptions BuildDdlTransactionOptions()
        => BuildDdlTransactionOptions(_connectionService.ActiveProfile?.DeveloperMode ?? false);

    // Standard Mode → write + read_committed + rec_version + NOWAIT (fail-fast,
    // identical to prior behaviour). Developer Mode → the same isolation but WAIT +
    // a lock timeout, so DDL waits for an in-use object to be released rather than
    // returning "object is in use" immediately. Pure + internal so a unit test pins
    // both shapes without a live Firebird. Affects ONLY the DDL path; data ops are
    // always NOWAIT.
    internal static FbTransactionOptions BuildDdlTransactionOptions(bool developerMode)
    {
        var behavior =
            FbTransactionBehavior.Write
            | FbTransactionBehavior.ReadCommitted
            | FbTransactionBehavior.RecVersion
            | (developerMode ? FbTransactionBehavior.Wait : FbTransactionBehavior.NoWait);

        var options = new FbTransactionOptions { TransactionBehavior = behavior };
        if (developerMode)
        {
            options.WaitTimeout = TimeSpan.FromSeconds(DdlLockTimeoutSeconds);
        }
        return options;
    }

    /// <summary>
    /// Splits a multi-statement DDL string into individual statements for the
    /// one-statement-per-<c>FbCommand</c> loop. Delegates to
    /// <see cref="SqlStatementSplitter"/> (Etap 2), which rides the shared parser's
    /// statement boundaries — the single authority for "what is a statement". Plain
    /// DDL/DML terminates at the next top-level <c>;</c>; a <c>CREATE/ALTER/RECREATE</c>
    /// of a <c>PROCEDURE/TRIGGER/FUNCTION/PACKAGE</c> stays whole (its DECLARE-section +
    /// body semicolons never split it — gotchas #55/#117/#128/#140/#152).
    /// <para>
    /// §0 (Paramount Law): the output is the exact DDL sent to the server, so it is
    /// byte-for-byte identical to the previous char-based splitter — pinned by a
    /// differential corpus test (old algorithm vs. the parser-backed splitter).
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> SplitStatements(string sql) => SqlStatementSplitter.Split(sql);
}

public sealed class DdlExecutionException : Exception
{
    public DdlExecutionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
