using System;
using System.Collections.Generic;

namespace EmberTern.Core.Performance;

/// <summary>Severity of a performance finding.</summary>
public enum FindingSeverity
{
    Info,
    Low,
    Medium,
    High,
}

/// <summary>What a finding is about. Phase 3a adds advisor observations grounded in measured
/// reads + catalog + predicate analysis; recommendation-bearing kinds (missing index) and any
/// fix actions remain a later phase.</summary>
public enum FindingKind
{
    CostlyFullScan,
    HighReadAmplification,
    LowSelectivityIndex,
    NonSargablePredicate,
    StaleStatistics,
    MissingIndexCandidate,
}

/// <summary>How confident the advisor is that a finding is real (not just plan-shaped noise).
/// Directly measured facts are High; findings that lean on the lightweight predicate parse or
/// an inferred plan→catalog link are Medium/Low. Surfaced as "High/Medium/Low confidence" so a
/// questionable finding reads as such rather than as a certainty.</summary>
public enum FindingConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>A single "Label: Value" evidence line under a finding.</summary>
public sealed record FindingEvidence(string Label, string Value);

/// <summary>One advisor finding — an observation grounded in MEASURED reads (+ catalog /
/// predicate analysis). It states what happened, why it matters, and what to investigate, but
/// carries NO recommendation / index suggestion / fix action (those are a later phase).</summary>
public sealed record Finding
{
    public required FindingKind Kind { get; init; }

    public required FindingSeverity Severity { get; init; }

    public required string Title { get; init; }

    public string Explanation { get; init; } = string.Empty;

    public IReadOnlyList<FindingEvidence> Evidence { get; init; } = Array.Empty<FindingEvidence>();

    /// <summary>The table this finding is about, when applicable.</summary>
    public string? Table { get; init; }

    /// <summary>The advisor rule that produced this finding (e.g. "R1", "R4"); empty for the
    /// legacy static path.</summary>
    public string RuleId { get; init; } = string.Empty;

    /// <summary>How confident the advisor is (see <see cref="FindingConfidence"/>).</summary>
    public FindingConfidence Confidence { get; init; } = FindingConfidence.Medium;
}
