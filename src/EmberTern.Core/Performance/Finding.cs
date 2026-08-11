using System;
using System.Collections.Generic;
using EmberTern.Core.Localization;

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

/// <summary>A single "Label: Value" evidence line under a finding.
///
/// <para>⭐ <b>The two halves have different owners, and etap C7 made that structural</b> — the same split C2
/// made for a Quick Info fact. The <b>label</b> is EmberTern's own word, so it is a <see cref="MessageKey"/>
/// the App resolves against the reader's language. The <b>value</b> stays a verbatim <c>string</c>: it is a
/// measured number, an index name, a SQL condition or a percentage — data, not language.</para></summary>
public sealed record FindingEvidence(MessageKey Label, string Value);

/// <summary>One advisor finding — an observation grounded in MEASURED reads (+ catalog /
/// predicate analysis). It states what happened, why it matters, and what to investigate, but
/// carries NO recommendation / index suggestion / fix action (those are a later phase).
///
/// <para>⭐ <b>Etap C7 — the text became <see cref="LocalizableMessage"/>, in the shape
/// <c>SessionHealthFinding</c> has carried since C1.</b> The two types are structural twins (kind · severity ·
/// title · explanation · evidence rows), so C7 moved a proven contract across rather than inventing one. The
/// rule produces a KEY and its DATA; the App resolves the words. ⛔ Do not put a composed sentence back into
/// this type — the moment a rule formats a string, the language is decided in Core.</para>
///
/// <para>⚠ <b>This type is a <c>record</c> (a class), not a <c>record struct</c>, so C5's trap does not apply
/// here</b> — and it is worth saying, because the same migration on <c>Diagnostic</c> turned on exactly that
/// difference. <see cref="LocalizableMessage"/>'s structural equality (C5) simply works.</para></summary>
public sealed record Finding
{
    public required FindingKind Kind { get; init; }

    public required FindingSeverity Severity { get; init; }

    public required LocalizableMessage Title { get; init; }

    /// <summary>Null when the finding has none. ⚠ Nullable rather than an "empty" message: a
    /// <see cref="MessageKey"/> refuses an empty token by construction, so there is no empty message to mean
    /// "absent" — the same shape as <c>SessionHealthFinding.Impact</c>.</summary>
    public LocalizableMessage? Explanation { get; init; }

    public IReadOnlyList<FindingEvidence> Evidence { get; init; } = Array.Empty<FindingEvidence>();

    /// <summary>The table this finding is about, when applicable.</summary>
    public string? Table { get; init; }

    /// <summary>The column this finding is about, when applicable (e.g. the predicate column for
    /// a missing-index or non-sargable finding) — lets a recommendation name it exactly.</summary>
    public string? Column { get; init; }

    /// <summary>The advisor rule that produced this finding (e.g. "R1", "R4"); empty for the
    /// legacy static path.</summary>
    public string RuleId { get; init; } = string.Empty;

    /// <summary>How confident the advisor is (see <see cref="FindingConfidence"/>).</summary>
    public FindingConfidence Confidence { get; init; } = FindingConfidence.Medium;
}
