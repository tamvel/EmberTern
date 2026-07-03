using System.Collections.Generic;

namespace EmberTern.Core.Performance;

/// <summary>One advisor rule. Given the assembled <see cref="PerformanceContext"/> it returns
/// zero or more findings. The single justified interface in the performance subsystem (many
/// impls — the <c>ISqlTemplate</c> precedent). Rules are pure: no I/O, no parsing, they only
/// read the context. Measured-first — a rule should lean on <see cref="PerformanceContext.Access"/>
/// and emit nothing rather than a questionable finding.</summary>
public interface IPerformanceRule
{
    /// <summary>Stable rule id (e.g. "R1"), also stamped onto the findings it produces.</summary>
    string Id { get; }

    IReadOnlyList<Finding> Evaluate(PerformanceContext context);
}
