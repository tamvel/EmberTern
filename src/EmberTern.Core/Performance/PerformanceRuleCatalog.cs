using System.Collections.Generic;
using EmberTern.Core.Performance.Rules;

namespace EmberTern.Core.Performance;

/// <summary>Composes the default advisor rule set (the <c>SqlTemplateCatalog</c> precedent).
/// Phase 3a: R1 (costly scan), R4 (low-selectivity index), R3 (non-sargable), R6 (high
/// amplification), R5 (stale stats). R2 (missing index) is Phase 3b — deliberately absent.</summary>
public static class PerformanceRuleCatalog
{
    public static IReadOnlyList<IPerformanceRule> Default() => new IPerformanceRule[]
    {
        new CostlyFullScanRule(), // R1
    };
}
