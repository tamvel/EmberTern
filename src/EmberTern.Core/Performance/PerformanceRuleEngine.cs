using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Performance;

/// <summary>Runs the advisor rule set over a <see cref="PerformanceContext"/> and returns the
/// combined findings, ranked most-severe-then-most-confident first (the Findings-zone order).
/// Pure Core.</summary>
public sealed class PerformanceRuleEngine
{
    private readonly IReadOnlyList<IPerformanceRule> _rules;

    public PerformanceRuleEngine(IEnumerable<IPerformanceRule>? rules = null)
        => _rules = (rules ?? PerformanceRuleCatalog.Default()).ToList();

    public IReadOnlyList<Finding> Evaluate(PerformanceContext context)
    {
        var findings = new List<Finding>();
        foreach (var rule in _rules)
        {
            findings.AddRange(rule.Evaluate(context));
        }
        return findings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Confidence)
            .ToList();
    }
}
