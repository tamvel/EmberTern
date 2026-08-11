using EmberTern.Core.Localization;

namespace EmberTern.Core.Performance;

/// <summary>A concise "potential improvement" attached to a finding — Phase 5. Recommendation-
/// ONLY: it says what improvement is worth reviewing, never a command / DDL / one-click. A single
/// line under the guidance. Inherits the originating finding's confidence (renders on the same
/// confidence-labelled card).</summary>
public sealed record Recommendation(MessageKey Heading, LocalizableMessage? Text)
{
    public static readonly Recommendation None = new(PerfMessages.RecommendationHeading, null);

    public bool HasText => Text is not null;
}

/// <summary>Derives the recommendation for a finding. It CONSUMES the finding (which only exists
/// because a rule's measured/catalog/predicate gates passed) — so recommendations are evidence-
/// derived, never from plan heuristics alone. Separate layer from findings + guidance
/// (Finding → Guidance → Recommendation). Pure, static. Language is "potential improvement /
/// worth reviewing / consider" — never must / required / guaranteed / fix / create / add index.
///
/// <para>⭐⭐ <b>Etap C7 fixed a defect here, ratified as D‑6.</b> The missing-index recommendation used to
/// substitute one of EmberTern's OWN NOUNS — <c>"the filtered column"</c> — as an argument when the column was
/// unknown. That is the shape C3 and C5 both ratified against: a noun dropped into a sentence works in English
/// and breaks in a language that inflects, because the argument cannot know which grammatical case the
/// sentence needs. There are now two whole-sentence keys, and the English wording of both is unchanged.</para></summary>
public static class RecommendationCatalog
{
    public static Recommendation For(Finding finding) => finding.Kind switch
    {
        FindingKind.CostlyFullScan => Line(PerfMessages.CostlyFullScanRecommendation),

        // ⛔ Two keys, never one key plus a noun — see the type's remarks (D‑6).
        FindingKind.MissingIndexCandidate => string.IsNullOrEmpty(finding.Column)
            ? Line(PerfMessages.MissingIndexRecommendationOnFilteredColumn)
            : Line(PerfMessages.MissingIndexRecommendationOnColumn, finding.Column),

        FindingKind.NonSargablePredicate => Line(PerfMessages.NonSargableRecommendation),

        FindingKind.LowSelectivityIndex => Line(PerfMessages.LowSelectivityRecommendation),

        FindingKind.StaleStatistics => Line(PerfMessages.StaleStatisticsRecommendation),

        FindingKind.HighReadAmplification => Line(PerfMessages.HighAmplificationRecommendation),

        _ => Recommendation.None,
    };

    private static Recommendation Line(MessageKey key, params object?[] arguments)
        => new(PerfMessages.RecommendationHeading, LocalizableMessage.Of(key, arguments));
}
