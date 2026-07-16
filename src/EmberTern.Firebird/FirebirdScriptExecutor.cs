using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Scripting;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Runs a parsed script as ONE caller-controlled transaction on the DATA lane — the same
/// connection F5 / Execute Procedure use, so DDL is co-located with the attachment that ran
/// it (gotcha #122). The transaction is the data working transaction owned by
/// <see cref="TransactionService"/>, so it surfaces in the existing transaction UI, naturally
/// gates the SQL Editor's F5 (one transaction per connection, gotcha #89), and is finalized
/// with the same Commit / Rollback.
///
/// Manual mode (the default) leaves the transaction OPEN after running every statement — the
/// user reviews the results grid, then calls <see cref="CommitAsync"/> or
/// <see cref="RollbackAsync"/>. There is NO per-statement autocommit (hard rule #3).
///
/// <para><b>KNOWN BROKEN — do not trust the old claim that "Firebird DDL is transactional, so a
/// mixed DDL+DML migration is genuinely all-or-nothing".</b> That was assumed, never measured, and
/// it is FALSE. Measured on FB5: a transaction CANNOT use an object it created but has not
/// committed — <c>CREATE TABLE T …; INSERT INTO T …;</c> in one transaction fails the INSERT with
/// <c>Table unknown (-204)</c>. Since every mode here runs the whole script in ONE transaction,
/// a mixed migration script — the very thing this tool exists for — cannot work today. Firebird
/// cannot both (a) let a transaction use an object it created and (b) keep that object
/// rollbackable; isql picks (a) via <c>SET AUTODDL ON</c>. Fixing this needs a real execution
/// policy (commit-after-DDL / AUTODDL, DDL-aware WAIT, up-front rejection of mixed scripts in
/// single-transaction mode) driven by the AST classifier
/// (<see cref="EmberTern.Core.Sql.SqlStatementClassifier"/>) rather than the driver's statement
/// enum. Deliberately deferred to its own sprint — see docs/history. This comment is the only
/// change made to this file by the SQL-Editor console refactor.</para>
///
/// The whole statement loop holds the connection's command lock (gotcha #31) so nothing
/// interleaves on the data connection mid-run; the lock is released before returning while the
/// transaction stays open, and Commit/Rollback re-acquire it via <see cref="TransactionService"/>.
/// </summary>
public sealed class FirebirdScriptExecutor
{
    /// <summary>Row-bearing statements are counted (not materialized) up to this cap.</summary>
    public const int RowCountCap = FirebirdQueryExecutor.DefaultRowLimit;

    private readonly FirebirdConnectionService _connectionService;
    private readonly TransactionService _transactionService;

    public FirebirdScriptExecutor(FirebirdConnectionService connectionService, TransactionService transactionService)
    {
        _connectionService = connectionService;
        _transactionService = transactionService;
    }

    /// <summary>
    /// Begins one transaction on the data lane and runs each statement into it, streaming per-
    /// statement results via <paramref name="progress"/>. Rejects up front (before starting a
    /// transaction) when a working transaction is already active or the script contains
    /// disallowed statements (transaction/session control — <see cref="ScriptValidation"/>).
    /// In <see cref="ScriptTransactionMode.Manual"/> the transaction is LEFT OPEN; in
    /// <see cref="ScriptTransactionMode.AutoCommitOnSuccess"/> it is committed when nothing
    /// failed, otherwise rolled back. Cancellation stops before the next statement (already-run
    /// ones remain in the open transaction for the user to Commit/Rollback).
    /// </summary>
    public async Task<ScriptRunOutcome> RunAsync(
        IReadOnlyList<ScriptStatement> statements,
        ScriptTransactionMode mode,
        bool stopOnError,
        IProgress<ScriptStatementResult>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statements);

        if (_transactionService.IsActive)
        {
            throw new ScriptExecutionException(
                "Commit or roll back the active transaction before running a script.");
        }

        var disallowed = ScriptValidation.FindDisallowed(statements);
        if (disallowed.Count > 0)
        {
            throw new ScriptExecutionException(
                "The script contains transaction-control or session statements " +
                "(COMMIT / ROLLBACK / SET TRANSACTION / CONNECT / SET NAMES / …) that can't run " +
                "under a managed script transaction. Remove them and try again.");
        }

        if (statements.Count == 0)
        {
            return new ScriptRunOutcome(Array.Empty<ScriptStatementResult>(), TransactionLeftOpen: false, AnyFailed: false, Cancelled: false);
        }

        try
        {
            await _transactionService.BeginTransactionAsync().ConfigureAwait(false);
        }
        catch (TransactionFailedException ex)
        {
            throw new ScriptExecutionException(ex.Message, ex);
        }

        var results = new List<ScriptStatementResult>(statements.Count);
        bool anyFailed = false;
        bool cancelled = false;

        var commandLock = _transactionService.CommandLock;
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = _transactionService.RequireOpenConnection();
            for (int i = 0; i < statements.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }

                var result = await RunOneAsync(connection, i, statements[i], cancellationToken).ConfigureAwait(false);
                results.Add(result);
                progress?.Report(result);

                if (!result.Success)
                {
                    anyFailed = true;
                    if (stopOnError) break;
                }
            }
        }
        finally
        {
            commandLock.Release();
        }

        bool leftOpen = true;
        if (mode == ScriptTransactionMode.AutoCommitOnSuccess && !cancelled)
        {
            if (anyFailed) await _transactionService.RollbackAsync().ConfigureAwait(false);
            else await _transactionService.CommitAsync().ConfigureAwait(false);
            leftOpen = false;
        }

        return new ScriptRunOutcome(results, leftOpen, anyFailed, cancelled);
    }

    // Runs one statement on the already-open script transaction. Row-bearing kinds go through
    // ExecuteReader (counting rows up to the cap without materializing a grid); everything else
    // through ExecuteNonQuery (RecordsAffected). A statement failure is captured as a result row
    // (not thrown) so the run continues (or stops, per stopOnError) and the transaction stays open.
    private async Task<ScriptStatementResult> RunOneAsync(
        FbConnection connection, int index, ScriptStatement statement, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = statement.Text;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _transactionService.ActiveTransaction;

            if (IsRowBearing(statement.Kind))
            {
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                int? rowCount = null;
                if (reader.FieldCount > 0)
                {
                    int count = 0;
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (++count >= RowCountCap) break;
                    }
                    rowCount = count;
                }
                sw.Stop();
                _transactionService.NotifyStatementExecuted();
                return new ScriptStatementResult(
                    index, statement.Text, statement.Kind, Success: true,
                    RecordsAffected: reader.RecordsAffected >= 0 ? reader.RecordsAffected : null,
                    RowCount: rowCount, sw.Elapsed, Error: null);
            }

            int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();
            _transactionService.NotifyStatementExecuted();
            return new ScriptStatementResult(
                index, statement.Text, statement.Kind, Success: true,
                RecordsAffected: affected >= 0 ? affected : null, RowCount: null, sw.Elapsed, Error: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FbException ex)
        {
            sw.Stop();
            return new ScriptStatementResult(
                index, statement.Text, statement.Kind, Success: false,
                RecordsAffected: null, RowCount: null, sw.Elapsed, Error: ex.Message);
        }
    }

    private static bool IsRowBearing(ScriptStatementKind kind)
        => kind is ScriptStatementKind.Select or ScriptStatementKind.ExecuteProcedure or ScriptStatementKind.ExecuteBlock;

    /// <summary>Commits the open script transaction (Manual mode, after the user reviews results).</summary>
    public Task CommitAsync() => _transactionService.CommitAsync();

    /// <summary>Rolls back the open script transaction (Manual mode, after the user reviews results).</summary>
    public Task RollbackAsync() => _transactionService.RollbackAsync();
}

public sealed class ScriptExecutionException : Exception
{
    public ScriptExecutionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
