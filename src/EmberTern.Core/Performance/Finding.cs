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

/// <summary>What a finding is about. Phase 2 produces only measured-reads observations;
/// recommendation-bearing kinds (missing index, etc.) arrive in a later phase.</summary>
public enum FindingKind
{
    CostlyFullScan,
    HighReadAmplification,
}

/// <summary>A single "Label: Value" evidence line under a finding.</summary>
public sealed record FindingEvidence(string Label, string Value);

/// <summary>One advisor finding. Phase 2: an observation grounded in MEASURED reads — it
/// states what happened and why it matters, but carries no recommendation / index
/// suggestion (those are a later phase).</summary>
public sealed record Finding
{
    public required FindingKind Kind { get; init; }

    public required FindingSeverity Severity { get; init; }

    public required string Title { get; init; }

    public string Explanation { get; init; } = string.Empty;

    public IReadOnlyList<FindingEvidence> Evidence { get; init; } = Array.Empty<FindingEvidence>();

    /// <summary>The table this finding is about, when applicable.</summary>
    public string? Table { get; init; }
}
