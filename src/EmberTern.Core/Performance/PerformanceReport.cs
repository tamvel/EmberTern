using System;
using System.Collections.Generic;

namespace EmberTern.Core.Performance;

/// <summary>The verdict-bar data: an at-a-glance grade + headline metrics.</summary>
public sealed record PerformanceVerdict
{
    public required PerformanceGrade Grade { get; init; }

    /// <summary>
    /// ⛔⛔ <b>DELIBERATELY still a <c>string</c> while every other Performance message became a
    /// <see cref="Localization.LocalizableMessage"/> in etap C7 — a NAMED EXEMPTION with a pinned premise, not
    /// an omission.</b>
    ///
    /// <para>Measured: <c>VerdictViewModel.Headline</c> exposes it and <c>PerformancePanelView.axaml</c> binds
    /// it <b>nowhere</b>. The panel shows <c>PerformanceInsight</c>'s grade line and lead instead, both of
    /// which are already localized App text. So these six sentences are produced, tested and never rendered.
    /// Localizing UI nobody can see would be building for a state that cannot occur (#346).</para>
    ///
    /// <para>⭐ The premise is asserted rather than trusted: <c>TheHeadline_IsStillBoundByNoSurface</c> scans
    /// the view, so the day someone binds it the test fails and asks for the migration. Same shape as C4b's
    /// <c>Settings.Import.NoMigrationStep</c> and C5's unreachable <c>ET0004</c> — guard the premise, not the
    /// policy (#322).</para>
    /// </summary>
    public required string Headline { get; init; }

    public TimeSpan Duration { get; init; }

    public long RowsReturned { get; init; }

    /// <summary>True for a result-producing SELECT; false for DML / EXECUTE PROCEDURE / BLOCK.</summary>
    public bool HasResultSet { get; init; } = true;

    /// <summary>Rows changed (insert + update + delete) — the meaningful output of a non-result
    /// statement; 0 for a SELECT.</summary>
    public long RowsChanged { get; init; }

    /// <summary>Rows read (per-table). Null when reads weren't captured.</summary>
    public long? RowsRead { get; init; }

    /// <summary>Rows read ÷ output rows (returned for a SELECT, changed for a DML/procedure).</summary>
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
