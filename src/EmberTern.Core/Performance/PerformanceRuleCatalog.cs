using System.Collections.Generic;
using EmberTern.Core.Performance.Rules;

namespace EmberTern.Core.Performance;

/// <summary>Composes the default advisor rule set (the <c>SqlTemplateCatalog</c> precedent).
/// Phase 3a: R1 (costly scan), R4 (low-selectivity index), R3 (non-sargable), R6 (high
/// amplification), R5 (stale stats). Phase 3b: R2 (missing-index candidate — finding-only,
/// no DDL). The engine ranks findings by severity then confidence, so registration order is
/// not significant.</summary>
public static class PerformanceRuleCatalog
{
    public static IReadOnlyList<IPerformanceRule> Default() => new IPerformanceRule[]
    {
        new CostlyFullScanRule(),       // R1
        new LowSelectivityIndexRule(),  // R4
        new NonSargablePredicateRule(), // R3
        new HighReadAmplificationRule(),// R6
        new StaleStatisticsRule(),      // R5
        new MissingIndexRule(),         // R2 (Phase 3b)
    };
}
