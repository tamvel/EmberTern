using System;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Performance;
using EmberTern.Core.Query;

namespace EmberTern.Firebird;

/// <summary>Orchestrates a profiled execution and returns a Core <see cref="PerformanceCapture"/>.
///
/// Phase 1 is a deliberate "Run &amp; profile": the query is executed (under the user's
/// manual data transaction, exactly like F5 — no autocommit), its wall-clock time and row
/// count are captured, and its plan is read best-effort. It does not feed the results grid;
/// it populates only the Performance panel. Later phases wrap MON$ before/after snapshots
/// around the execution inside this same method without changing its signature.</summary>
public sealed class PerformanceProfiler
{
    private readonly FirebirdQueryExecutor _executor;
    private readonly FirebirdPlanReader _planReader;

    public PerformanceProfiler(FirebirdQueryExecutor executor, FirebirdPlanReader planReader)
    {
        _executor = executor;
        _planReader = planReader;
    }

    public async Task<PerformanceCapture> ProfileAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new PerformanceCaptureException("Query is empty.");
        }

        // Execute first: a real execution error surfaces through the normal executor path
        // (QueryExecutionException). The plan is only worth reading for a query that ran.
        QueryResult result = await _executor.ExecuteAsync(sql, cancellationToken).ConfigureAwait(false);

        // Plan is best-effort — a capture failure degrades to "no plan", never fails the run.
        RawPlanCapture? plan = null;
        TimeSpan? prepare = null;
        try
        {
            var planResult = await _planReader.GetPlanAsync(sql, cancellationToken).ConfigureAwait(false);
            plan = planResult.Plan;
            prepare = planResult.PrepareTime > TimeSpan.Zero ? planResult.PrepareTime : null;
        }
        catch (PerformanceCaptureException)
        {
            // leave plan null
        }

        var timings = new ExecutionTimings { Prepare = prepare, Execute = result.Elapsed };

        return new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = sql },
            Plan = plan,
            Timings = timings,
            RowsReturned = result.Rows.Count,
            Truncated = result.Truncated,
            RecordsAffected = result.RecordsAffected,
            Method = CaptureMethod.PlanOnly,
        };
    }
}
