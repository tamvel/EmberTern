using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Scripting;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Runs a parsed script as ONE caller-controlled transaction on the DATA lane. The transaction is
/// the data working transaction owned by <see cref="TransactionService"/>, so it surfaces in the
/// existing transaction UI, naturally gates the SQL Editor's F5 (one transaction per connection,
/// gotcha #89), and is finalized with the same Commit / Rollback.
///
/// Manual mode (the default) leaves the transaction OPEN after running every statement — the
/// user reviews the results grid, then calls <see cref="CommitAsync"/> or
/// <see cref="RollbackAsync"/>. There is NO per-statement autocommit (hard rule #3).
///
/// <para><b>Developer Mode.</b> An ALL-DDL script under auto-commit begins its transaction with the
/// Developer-Mode-aware DDL wait policy (<see cref="FirebirdDdlExecutor.BuildDdlTransactionOptions(bool)"/>)
/// instead of the working transaction's NOWAIT default, so deploying objects that other sessions are
/// using waits for them to be released rather than failing instantly — the same behaviour the object
/// editors' Compile has always had. Both conditions are load-bearing and are explained on
/// <see cref="UsesDeveloperModeWaitPolicy"/>. Everything else — one lane, one transaction, no
/// per-statement commits, no routing by statement kind (gotcha #215) — is unchanged; this is one TPB
/// flag chosen at BEGIN, not an execution model.</para>
///
/// <para><b>Mixed DDL+DML migrations — single-transaction modes still cannot run them; use
/// <see cref="ScriptTransactionMode.Sequenced"/>.</b> Firebird DDL is <em>not</em> "all-or-nothing":
/// a transaction CANNOT use an object it created but has not committed — <c>CREATE TABLE T …;
/// INSERT INTO T …;</c> in ONE transaction fails the INSERT with <c>Table unknown (-204)</c>
/// (measured on FB5, gotcha #213). So <see cref="ScriptTransactionMode.Manual"/> and
/// <see cref="ScriptTransactionMode.AutoCommitOnSuccess"/> — which run the whole script in one
/// transaction — still cannot run a mixed migration. That is exactly what <c>Sequenced</c> fixes
/// (<see cref="RunSequencedAsync"/>): the AST classifier
/// (<see cref="EmberTern.Core.Sql.SqlStatementClassifier"/>, via
/// <see cref="ScriptSegmentPlanner"/>) splits the script into per-transaction segments committed
/// one at a time, so a later statement sees an object an earlier segment created — what isql does
/// with <c>SET AUTODDL ON</c>, and live-verified (Step 4 seam B: tools/probes/ScriptExecutorSequencedProbe).
/// Up-front rejection of a mixed script in a single-transaction mode (so the user is pointed at
/// <c>Sequenced</c> rather than failing on statement 2) is Step 5, in the App layer.</para>
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
    /// <para><see cref="ScriptTransactionMode.Sequenced"/> takes a different execution shape entirely
    /// — many transactions, one committed segment at a time — after the same shared up-front checks;
    /// see <see cref="RunSequencedAsync"/>.</para>
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

        // Sequenced deployment: many transactions, one segment at a time (§5). Everything above —
        // the active-transaction guard, the disallowed-statement check, the empty short-circuit —
        // is shared; only the execution shape differs.
        if (mode == ScriptTransactionMode.Sequenced)
        {
            return await RunSequencedAsync(statements, stopOnError, progress, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await _transactionService.BeginTransactionAsync(ResolveTransactionOptions(statements, mode))
                .ConfigureAwait(false);
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

    /// <summary>
    /// Runs the script as a SEQUENCE of segments (<see cref="ScriptTransactionMode.Sequenced"/>). The
    /// planner (<see cref="ScriptSegmentPlanner"/>) splits it into ordered segments; each runs in its
    /// OWN transaction on the data lane — begun with the segment's
    /// <see cref="SegmentTransactionPolicy"/> TPB (<see cref="ResolveSegmentTransactionOptions"/>),
    /// committed on success, rolled back on failure. Exactly one transaction is open at a time, so a
    /// later statement sees an object an earlier segment created (gotcha #213, fixed by design) and a
    /// segment can never block on our own still-open work. Committed segments STAY applied if a later
    /// one fails — the honest, non-atomic cost of a mixed migration, surfaced through the per-statement
    /// results (the App reconstructs segment boundaries in Step 5). The Firebird layer only EXECUTES
    /// the prepared plan; it never decides a segment's shape.
    /// <para>Nothing is ever left open (every begun segment is committed or rolled back), so the
    /// outcome's <c>TransactionLeftOpen</c> is always false — Sequenced is never the "review then
    /// Commit" flow.</para>
    /// </summary>
    private async Task<ScriptRunOutcome> RunSequencedAsync(
        IReadOnlyList<ScriptStatement> statements,
        bool stopOnError,
        IProgress<ScriptStatementResult>? progress,
        CancellationToken cancellationToken)
    {
        var plan = ScriptSegmentPlanner.Plan(statements);
        bool developerMode = _connectionService.ActiveProfile?.DeveloperMode ?? false;

        var results = new List<ScriptStatementResult>(statements.Count);
        bool anyFailed = false;
        bool cancelled = false;
        // The original index into `statements`; segments are contiguous and in source order, so a
        // running counter reconstructs each statement's real index without the segment carrying one.
        int globalIndex = 0;

        foreach (var segment in plan)
        {
            if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }

            try
            {
                await _transactionService
                    .BeginTransactionAsync(ResolveSegmentTransactionOptions(segment.Policy, developerMode))
                    .ConfigureAwait(false);
            }
            catch (TransactionFailedException ex)
            {
                throw new ScriptExecutionException(ex.Message, ex);
            }

            bool segmentFailed = false;
            bool stopRequested = false;

            // Hold the command lock only around this segment's statements — Begin/Commit/Rollback
            // acquire it themselves, so holding it across them would deadlock (the single-tx path
            // above releases before committing for the same reason).
            var commandLock = _transactionService.CommandLock;
            await commandLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var connection = _transactionService.RequireOpenConnection();
                foreach (var statement in segment.Statements)
                {
                    if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }

                    var result = await RunOneAsync(connection, globalIndex, statement, cancellationToken)
                        .ConfigureAwait(false);
                    globalIndex++;
                    results.Add(result);
                    progress?.Report(result);

                    if (!result.Success)
                    {
                        segmentFailed = true;
                        anyFailed = true;
                        // stopOnError → stop the whole run now. Otherwise keep running this segment's
                        // remaining statements — they roll back with it, exactly as AutoCommit runs a
                        // whole script then rolls back (a schema segment is a singleton, so it has no
                        // "rest"; only a data segment ever continues past a failure here).
                        if (stopOnError) { stopRequested = true; break; }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled mid-statement — fall through to settle (roll back) the open segment so it
                // is never leaked, then stop.
                cancelled = true;
            }
            finally
            {
                commandLock.Release();
            }

            if (segmentFailed || cancelled)
            {
                await _transactionService.RollbackAsync().ConfigureAwait(false);
            }
            else
            {
                await _transactionService.CommitAsync().ConfigureAwait(false);
            }

            if (stopRequested || cancelled) break;
        }

        return new ScriptRunOutcome(results, TransactionLeftOpen: false, AnyFailed: anyFailed, Cancelled: cancelled);
    }

    /// <summary>
    /// The TPB the script transaction begins with: the Developer-Mode-aware DDL policy when
    /// <see cref="UsesDeveloperModeWaitPolicy"/> says so, otherwise null = the working
    /// transaction's usual NOWAIT default. Reads Developer Mode from the active profile, exactly
    /// like <see cref="FirebirdDdlExecutor"/> does.
    /// </summary>
    private FbTransactionOptions? ResolveTransactionOptions(
        IReadOnlyList<ScriptStatement> statements, ScriptTransactionMode mode)
        => UsesDeveloperModeWaitPolicy(statements, mode)
            // Reuse the DDL executor's builder — one definition of "the Dev Mode wait policy",
            // shared by Compile and by this. Standard = short wait, Developer = long (gotcha #214).
            ? FirebirdDdlExecutor.BuildDdlTransactionOptions(
                _connectionService.ActiveProfile?.DeveloperMode ?? false)
            : null;

    /// <summary>
    /// Whether this run gets the Developer-Mode DDL wait policy instead of the working
    /// transaction's NOWAIT default. True only when BOTH hold:
    /// <list type="bullet">
    /// <item>every statement is DDL — a transaction's wait policy is fixed at BEGIN and cannot vary
    /// per statement, so this is what guarantees no DML is ever made to WAIT; and</item>
    /// <item>the mode settles the transaction inside the run (<see cref="ScriptTransactionMode.AutoCommitOnSuccess"/>)
    /// — in Manual the transaction is left OPEN and the SQL Editor's next F5 joins it
    /// (<see cref="TransactionService.BeginTransactionAsync"/> early-returns when one is active),
    /// which would silently give the console a WAIT transaction.</item>
    /// </list>
    /// An empty script gets no special policy. Pure + internal so the decision is unit-pinned
    /// without a live Firebird.
    /// <para>This does NOT decide where or how anything executes — one lane, one transaction, no
    /// per-statement commits (gotcha #215 stands). It decides one TPB flag at BEGIN.</para>
    /// </summary>
    internal static bool UsesDeveloperModeWaitPolicy(
        IReadOnlyList<ScriptStatement> statements, ScriptTransactionMode mode)
        => mode == ScriptTransactionMode.AutoCommitOnSuccess
        && statements.Count > 0
        && statements.All(s => s.Kind == ScriptStatementKind.Ddl);

    /// <summary>
    /// The TPB a <see cref="ScriptTransactionMode.Sequenced"/> segment begins with, from the
    /// <see cref="SegmentTransactionPolicy"/> the planner assigned it. A
    /// <see cref="SegmentTransactionPolicy.SchemaWait"/> segment reuses the SAME
    /// Developer-Mode-aware WAIT policy object-editor Compile uses
    /// (<see cref="FirebirdDdlExecutor.BuildDdlTransactionOptions(bool)"/> — one definition of "the
    /// Dev Mode wait policy", never a copy that can drift), so deploying an object another SESSION
    /// holds waits for it instead of failing instantly. A
    /// <see cref="SegmentTransactionPolicy.DataNoWait"/> segment returns <c>null</c> = the working
    /// transaction's usual NOWAIT ReadCommitted default, so a deployment's DML never blocks on an
    /// ordinary row lock. Pure + internal so the mapping is unit-pinned without a live Firebird.
    /// <para>This only MAPS a plan the planner already made — it never decides a segment's kind
    /// (<see cref="ScriptSegmentPlanner"/> is the sole planner; the Firebird layer just executes
    /// the prepared plan). Consumed by the Sequenced execution loop (Step 4 seam B).</para>
    /// </summary>
    internal static FbTransactionOptions? ResolveSegmentTransactionOptions(
        SegmentTransactionPolicy policy, bool developerMode)
        => policy == SegmentTransactionPolicy.SchemaWait
            ? FirebirdDdlExecutor.BuildDdlTransactionOptions(developerMode)
            : null;

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
