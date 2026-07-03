using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql;

namespace EmberTern.Core.Performance;

/// <summary>Assembles a <see cref="PerformanceContext"/> from a capture + the already-parsed
/// plan/access + an optional catalog: extracts predicates from the SQL, classifies their
/// sargability, and computes amplification. Pure Core.</summary>
public static class PerformanceContextBuilder
{
    public static PerformanceContext Build(
        PerformanceCapture capture,
        PlanTree? plan,
        TableAccessProfile? access,
        CatalogModel? catalog)
    {
        var predicates = PredicateExtractor.Extract(capture.Statement.Sql);
        var sargability = predicates.Select(SargabilityClassifier.Classify).ToList();

        long returned = capture.HasResultSet ? capture.RowsReturned : 0;
        long? read = access?.TotalRowsRead;
        double? amplification = (access is not null && returned > 0)
            ? (double)access.TotalRowsRead / returned
            : null;

        return new PerformanceContext
        {
            Capture = capture,
            Plan = plan,
            Access = access,
            RowsReturned = returned,
            RowsRead = read,
            Amplification = amplification,
            Predicates = predicates,
            Sargability = sargability,
            Catalog = catalog ?? CatalogModel.Empty,
        };
    }
}
