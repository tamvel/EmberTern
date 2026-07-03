using System;
using System.Collections.Generic;
using EmberTern.Core.Performance;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// Pins the catalog SQL shape (verified against live FB 5.0.3) + the pure cardinality
// estimate. The live reads are smoke-verified against a real engine.
public class FirebirdCatalogReaderTests
{
    [Fact]
    public void IndexHeaderSql_ReadsIndicesWithStatsUniquenessInactiveExpressionAndPk()
    {
        var sql = FirebirdCatalogReader.BuildIndexHeaderSql(includeCondition: true);
        Assert.Contains("RDB$INDICES", sql);
        Assert.Contains("RDB$UNIQUE_FLAG", sql);
        Assert.Contains("RDB$INDEX_INACTIVE", sql);
        Assert.Contains("RDB$STATISTICS", sql);
        Assert.Contains("RDB$EXPRESSION_SOURCE", sql);
        Assert.Contains("'PRIMARY KEY'", sql);
        Assert.Contains("i.RDB$RELATION_NAME = @tableName", sql);
    }

    [Fact]
    public void IndexHeaderSql_IncludesConditionSourceOnlyWhenFb5()
    {
        Assert.Contains("RDB$CONDITION_SOURCE", FirebirdCatalogReader.BuildIndexHeaderSql(includeCondition: true));
        // FB3/4: the column doesn't exist → must NOT be referenced (CAST(NULL) placeholder instead).
        var pre5 = FirebirdCatalogReader.BuildIndexHeaderSql(includeCondition: false);
        Assert.DoesNotContain("RDB$CONDITION_SOURCE", pre5);
        Assert.Contains("CAST(NULL", pre5);
    }

    [Fact]
    public void SegmentsSql_JoinsSegmentsToIndicesOrderedByPosition()
    {
        var sql = FirebirdCatalogReader.SegmentsSql;
        Assert.Contains("RDB$INDEX_SEGMENTS", sql);
        Assert.Contains("RDB$INDICES", sql);
        Assert.Contains("i.RDB$RELATION_NAME = @tableName", sql);
        Assert.Contains("ORDER BY s.RDB$INDEX_NAME, s.RDB$FIELD_POSITION", sql);
    }

    private static IndexModel Idx(bool unique, double? selectivity, bool inactive = false)
        => new() { Name = "IX", IsUnique = unique, IsInactive = inactive, Selectivity = selectivity };

    [Fact]
    public void EstimateCardinality_FromUniqueIndexSelectivity()
        => Assert.Equal(100, FirebirdCatalogReader.EstimateCardinality(new[] { Idx(true, 0.01) }));

    [Fact]
    public void EstimateCardinality_PicksSmallestPositiveSelectivity()
        => Assert.Equal(1000, FirebirdCatalogReader.EstimateCardinality(new[] { Idx(true, 0.01), Idx(true, 0.001) }));

    [Fact]
    public void EstimateCardinality_IgnoresNonUniqueInactiveAndUncomputed()
    {
        Assert.Null(FirebirdCatalogReader.EstimateCardinality(new[] { Idx(false, 0.0001) }));       // non-unique
        Assert.Null(FirebirdCatalogReader.EstimateCardinality(new[] { Idx(true, 0.01, inactive: true) }));
        Assert.Null(FirebirdCatalogReader.EstimateCardinality(new[] { Idx(true, null) }));           // uninitialized stats
        Assert.Null(FirebirdCatalogReader.EstimateCardinality(Array.Empty<IndexModel>()));
    }
}
