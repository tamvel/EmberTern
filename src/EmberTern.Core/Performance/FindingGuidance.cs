using System;
using System.Collections.Generic;

namespace EmberTern.Core.Performance;

/// <summary>A concise "what to investigate next" block attached to a finding — a few
/// investigation-oriented bullets, no prescription. Phase 4 (guidance) layer.</summary>
public sealed record FindingGuidance(string Heading, IReadOnlyList<string> Items)
{
    public static readonly FindingGuidance None = new(string.Empty, Array.Empty<string>());

    public bool HasItems => Items.Count > 0;
}

/// <summary>Maps a <see cref="FindingKind"/> to its investigation guidance. Pure, static, and
/// derived from the kind only — so it adds nothing to the rules or the <see cref="Finding"/>
/// shape, and the guidance a finding shows always matches its kind. Language is strictly
/// investigation-oriented (Review / Check / Consider / Verify) — no imperatives, no DDL, no
/// "create/add index/fix/execute". Guidance inherits the originating finding's confidence
/// (it renders on the same confidence-labelled card).</summary>
public static class FindingGuidanceCatalog
{
    private const string Heading = "What to investigate";

    public static FindingGuidance For(FindingKind kind) => kind switch
    {
        FindingKind.CostlyFullScan => new FindingGuidance(Heading, new[]
        {
            "Check the filter's selectivity — how many rows it actually keeps.",
            "Check whether a suitable index exists on the filtered column(s).",
            "Review the table size and the query's access pattern.",
        }),

        FindingKind.MissingIndexCandidate => new FindingGuidance(Heading, new[]
        {
            "Review the filtered columns and the order they're used in.",
            "Review existing index coverage for those columns.",
            "Verify the query runs often enough to justify a new index.",
        }),

        FindingKind.NonSargablePredicate => new FindingGuidance(Heading, new[]
        {
            "Review the expression applied to the indexed column.",
            "Check whether the condition can reference the column directly.",
            "Consider whether a computed (expression) index would be appropriate.",
        }),

        FindingKind.LowSelectivityIndex => new FindingGuidance(Heading, new[]
        {
            "Review the column's value distribution (how many distinct values).",
            "Review composite-index opportunities with a more selective leading column.",
            "Check whether a different access path suits this query.",
        }),

        FindingKind.StaleStatistics => new FindingGuidance(Heading, new[]
        {
            "Review the index statistics for this table.",
            "Verify whether the statistics are up to date.",
        }),

        FindingKind.HighReadAmplification => new FindingGuidance(Heading, new[]
        {
            "Review the join breadth — how many rows the joins expand to.",
            "Review the sub-queries the plan evaluates.",
            "Review the filtering effectiveness — where rows are read but discarded.",
        }),

        _ => FindingGuidance.None,
    };
}
