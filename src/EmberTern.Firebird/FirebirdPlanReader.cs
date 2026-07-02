using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Performance;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>Captures the execution plan for a statement by preparing (not executing) a
/// command on the data lane, then reading the driver's plan. Prefers the Explain plan
/// (FB3+); falls back to the Legacy plan. Preparing is a side-effect-free metadata
/// compile — no rows are produced. Returns Core DTOs only; holds all FbCommand internally.
///
/// Isolated from <c>PerformanceProfiler</c> because a later phase reuses it to read PSQL
/// cursor plans from MON$COMPILED_STATEMENTS (FB5).</summary>
public sealed class FirebirdPlanReader
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly TransactionService? _transactionService;

    public FirebirdPlanReader(FirebirdConnectionService connectionService, TransactionService? transactionService = null)
    {
        _connectionService = connectionService;
        _transactionService = transactionService;
    }

    /// <summary>Prepares <paramref name="sql"/> and returns its plan (or null when no plan
    /// could be read) together with the measured prepare time. A hard prepare failure
    /// (e.g. invalid SQL) is wrapped as <see cref="PerformanceCaptureException"/>.</summary>
    public async Task<PlanCaptureResult> GetPlanAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new PlanCaptureResult(null, TimeSpan.Zero);
        }

        var connection = _transactionService?.RequireOpenConnection() ?? _connectionService.RequireOpenConnection();
        // Capture the lock once (gotcha #120): the lane accessor can flip mid-call, and
        // releasing a different semaphore than we acquired permanently leaks the held one.
        var commandLock = _transactionService?.CommandLock ?? _connectionService.CommandLock;
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            // Readers never open their own transaction: attach to the user's working tx
            // when active, else the managed driver prepares under an implicit tx.
            if (_transactionService?.ActiveTransaction is { } tx)
            {
                cmd.Transaction = tx;
            }

            var sw = Stopwatch.StartNew();
            await cmd.PrepareAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();

            var plan = await TryReadPlanAsync(cmd, cancellationToken).ConfigureAwait(false);
            return new PlanCaptureResult(plan, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FbException ex)
        {
            throw new PerformanceCaptureException(ex.Message?.Trim() ?? "Failed to capture the execution plan.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new PerformanceCaptureException(ex.Message, ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    private static async Task<RawPlanCapture?> TryReadPlanAsync(FbCommand cmd, CancellationToken cancellationToken)
    {
        // Explain plan first (FB3+). Any driver-side hiccup degrades to Legacy, then null,
        // rather than failing the whole profile — the plan is best-effort.
        try
        {
            var explained = await cmd.GetCommandExplainedPlanAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(explained))
            {
                return new RawPlanCapture(PlanDialect.Explain, explained.Trim());
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FbException)
        {
            // fall through to legacy
        }

        try
        {
            var legacy = await cmd.GetCommandPlanAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                return new RawPlanCapture(PlanDialect.Legacy, legacy.Trim());
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FbException)
        {
            // no plan available
        }

        return null;
    }
}

/// <summary>The plan (or null) plus the measured prepare time.</summary>
public sealed record PlanCaptureResult(RawPlanCapture? Plan, TimeSpan PrepareTime);
