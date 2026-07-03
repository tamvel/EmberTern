using System;

namespace EmberTern.Core.Performance;

/// <summary>The single Core entry point the App calls: capture in, report out. In Phase 1
/// this composes the plan parser + report builder. Phase 2 adds the rule engine and an
/// optional catalog argument behind this same method, so the App's call site does not
/// change when findings/recommendations are introduced.</summary>
public sealed class PerformanceAnalyzer
{
    private readonly PerformanceReportBuilder _builder;

    public PerformanceAnalyzer(PerformanceReportBuilder? builder = null)
        => _builder = builder ?? new PerformanceReportBuilder();

    public PerformanceReport Analyze(PerformanceCapture capture, CatalogModel? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return _builder.Build(capture, catalog);
    }
}
