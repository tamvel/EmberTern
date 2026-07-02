using System;
using System.Collections.Generic;

namespace EmberTern.Core.Performance;

/// <summary>Everything captured for one profiled statement, before analysis. Produced by
/// the Firebird <c>PerformanceProfiler</c> (Core-typed only), consumed by the Core
/// analyzer. Phase 1 fills plan + timings + row count; per-table reads, I/O counters and
/// PSQL cursor children are added additively in later phases.</summary>
public sealed class PerformanceCapture
{
    public required StatementIdentity Statement { get; init; }

    /// <summary>Raw plan text + dialect, or null when the plan could not be captured.</summary>
    public RawPlanCapture? Plan { get; init; }

    public ExecutionTimings? Timings { get; init; }

    /// <summary>Rows returned by a result-set statement (respecting the executor cap).</summary>
    public long RowsReturned { get; init; }

    /// <summary>True when the executor returned a truncated (capped) result set.</summary>
    public bool Truncated { get; init; }

    /// <summary>Records affected for a non-result DML statement; null for SELECTs.</summary>
    public int? RecordsAffected { get; init; }

    public CaptureMethod Method { get; init; } = CaptureMethod.PlanOnly;

    /// <summary>Internal PSQL cursors (procedure/function breakdown). Empty in Phase 1.</summary>
    public IReadOnlyList<PerformanceCapture> Cursors { get; init; } = Array.Empty<PerformanceCapture>();

    public bool PlanAvailable => Plan is not null;

    public bool HasResultSet => RecordsAffected is null;
}
