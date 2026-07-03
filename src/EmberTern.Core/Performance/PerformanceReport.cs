using System;
using System.Collections.Generic;

namespace EmberTern.Core.Performance;

/// <summary>The verdict-bar data: an at-a-glance grade + headline metrics.</summary>
public sealed record PerformanceVerdict
{
    public required PerformanceGrade Grade { get; init; }

    public required string Headline { get; init; }

    public TimeSpan Duration { get; init; }

    public long RowsReturned { get; init; }

    /// <summary>Rows read (per-table). Null until Phase 2 supplies per-table reads.</summary>
    public long? RowsRead { get; init; }

    /// <summary>Rows read ÷ rows returned. Null until Phase 2.</summary>
    public double? Amplification { get; init; }
}

/// <summary>The expert "details" drawer data.</summary>
public sealed record ExecutionDetails
{
    public ExecutionTimings? Timings { get; init; }

    public string? RawPlanText { get; init; }

    public PlanDialect? PlanDialect { get; init; }

    public CaptureMethod Method { get; init; } = CaptureMethod.PlanOnly;
}

/// <summary>The aggregate output of a performance analysis for one execution. Phase 1
/// fills the verdict, the parsed plan and the details; findings, table-access profile,
/// PSQL cursor rollup and function call profile are added additively in later phases so
/// this aggregate never has to be restructured.</summary>
public sealed record PerformanceReport
{
    public required PerformanceVerdict Verdict { get; init; }

    /// <summary>Parsed plan tree, or null when no plan could be captured/parsed.</summary>
    public PlanTree? Plan { get; init; }

    /// <summary>Measured per-table access, or null when reads were not captured. Phase 2.</summary>
    public TableAccessProfile? Access { get; init; }

    /// <summary>Reads-based findings (empty when no reads / nothing notable). Phase 2.</summary>
    public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();

    public required ExecutionDetails Details { get; init; }
}
