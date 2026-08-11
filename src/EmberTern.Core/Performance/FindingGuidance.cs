using System;
using System.Collections.Generic;
using EmberTern.Core.Localization;

namespace EmberTern.Core.Performance;

/// <summary>A concise "what to investigate next" block attached to a finding — a few
/// investigation-oriented bullets, no prescription. Phase 4 (guidance) layer.
///
/// <para>⭐ Etap C7: heading and items are <see cref="MessageKey"/>s. They carry no arguments at all, which is
/// why this group has the cleanest possible proof of zero text change — the 18 old literals and the 18 new
/// resource values are compared byte for byte (the C2 shape).</para></summary>
public sealed record FindingGuidance(MessageKey Heading, IReadOnlyList<MessageKey> Items)
{
    /// <summary>⚠ The heading is a real key rather than an empty one: <see cref="MessageKey"/> refuses an
    /// empty token, and nothing reads the heading of a guidance block with no items — <see cref="HasItems"/>
    /// is the only gate, and it behaves exactly as before.</summary>
    public static readonly FindingGuidance None =
        new(PerfMessages.GuidanceHeading, Array.Empty<MessageKey>());

    public bool HasItems => Items.Count > 0;
}

/// <summary>Maps a <see cref="FindingKind"/> to its investigation guidance. Pure, static, and
/// derived from the kind only — so it adds nothing to the rules or the <see cref="Finding"/>
/// shape, and the guidance a finding shows always matches its kind. Language is strictly
/// investigation-oriented (Review / Check / Consider / Verify) — no imperatives, no DDL, no
/// "create/add index/fix/execute". Guidance inherits the originating finding's confidence
/// (it renders on the same confidence-labelled card).
///
/// <para>⚠ That "no imperatives, no DDL" rule is a product decision this module has always carried, and until
/// C7 it was checked on exactly one finding by two assertions in <c>PerformanceMissingIndexTests</c>. It is
/// now checked across every Performance sentence in the catalog by
/// <c>NoPerfSentence_UsesImperativeOrDdlVocabulary</c>.</para></summary>
public static class FindingGuidanceCatalog
{
    public static FindingGuidance For(FindingKind kind) => kind switch
    {
        FindingKind.CostlyFullScan => new FindingGuidance(PerfMessages.GuidanceHeading, new[]
        {
            PerfMessages.CostlyFullScanGuidance1,
            PerfMessages.CostlyFullScanGuidance2,
            PerfMessages.CostlyFullScanGuidance3,
        }),

        FindingKind.MissingIndexCandidate => new FindingGuidance(PerfMessages.GuidanceHeading, new[]
        {
            PerfMessages.MissingIndexGuidance1,
            PerfMessages.MissingIndexGuidance2,
            PerfMessages.MissingIndexGuidance3,
        }),

        FindingKind.NonSargablePredicate => new FindingGuidance(PerfMessages.GuidanceHeading, new[]
        {
            PerfMessages.NonSargableGuidance1,
            PerfMessages.NonSargableGuidance2,
            PerfMessages.NonSargableGuidance3,
        }),

        FindingKind.LowSelectivityIndex => new FindingGuidance(PerfMessages.GuidanceHeading, new[]
        {
            PerfMessages.LowSelectivityGuidance1,
            PerfMessages.LowSelectivityGuidance2,
            PerfMessages.LowSelectivityGuidance3,
        }),

        FindingKind.StaleStatistics => new FindingGuidance(PerfMessages.GuidanceHeading, new[]
        {
            PerfMessages.StaleStatisticsGuidance1,
            PerfMessages.StaleStatisticsGuidance2,
        }),

        FindingKind.HighReadAmplification => new FindingGuidance(PerfMessages.GuidanceHeading, new[]
        {
            PerfMessages.HighAmplificationGuidance1,
            PerfMessages.HighAmplificationGuidance2,
            PerfMessages.HighAmplificationGuidance3,
        }),

        _ => FindingGuidance.None,
    };
}
