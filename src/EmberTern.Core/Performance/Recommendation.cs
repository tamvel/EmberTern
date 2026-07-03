using System.Globalization;

namespace EmberTern.Core.Performance;

/// <summary>A concise "potential improvement" attached to a finding — Phase 5. Recommendation-
/// ONLY: it says what improvement is worth reviewing, never a command / DDL / one-click. A single
/// line under the guidance. Inherits the originating finding's confidence (renders on the same
/// confidence-labelled card).</summary>
public sealed record Recommendation(string Heading, string Text)
{
    public static readonly Recommendation None = new(string.Empty, string.Empty);

    public bool HasText => !string.IsNullOrEmpty(Text);
}

/// <summary>Derives the recommendation for a finding. It CONSUMES the finding (which only exists
/// because a rule's measured/catalog/predicate gates passed) — so recommendations are evidence-
/// derived, never from plan heuristics alone. Separate layer from findings + guidance
/// (Finding → Guidance → Recommendation). Pure, static. Language is "potential improvement /
/// worth reviewing / consider" — never must / required / guaranteed / fix / create / add index.</summary>
public static class RecommendationCatalog
{
    private const string Heading = "Potential improvement";

    public static Recommendation For(Finding finding) => finding.Kind switch
    {
        FindingKind.CostlyFullScan => new Recommendation(Heading,
            "Review whether an index on the filtered column would reduce this scan."),

        FindingKind.MissingIndexCandidate => new Recommendation(Heading, string.Format(
            CultureInfo.CurrentCulture,
            "Review whether an index on {0} would benefit this workload.",
            string.IsNullOrEmpty(finding.Column) ? "the filtered column" : finding.Column)),

        FindingKind.NonSargablePredicate => new Recommendation(Heading,
            "Review whether the expression can be removed from the indexed column so an index can be used."),

        FindingKind.LowSelectivityIndex => new Recommendation(Heading,
            "Review whether a more selective leading column exists for this index."),

        FindingKind.StaleStatistics => new Recommendation(Heading,
            "Review whether the index statistics should be refreshed."),

        FindingKind.HighReadAmplification => new Recommendation(Heading,
            "Review the join breadth and sub-query strategy."),

        _ => Recommendation.None,
    };
}
