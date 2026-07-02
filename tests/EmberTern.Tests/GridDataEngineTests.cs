using System;
using System.Collections.Generic;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

// Faza 1 — client-side filter + aggregation engine (pure, no UI / DB). Pins the
// Firebird-matching semantics so materialized and server-paged grids agree.
public class GridDataEngineTests
{
    private static readonly QueryColumn[] Cols =
    {
        new("AMOUNT", typeof(decimal)),
        new("NAME", typeof(string)),
        new("CREATED", typeof(DateTime)),
        new("ACTIVE", typeof(bool)),
    };

    private static GridFilter One(int col, string name, GridFilterOperator op, string? val, GridFilterCombine combine = GridFilterCombine.And)
        => new(new[] { new GridFilterCondition(col, name, op, val) }, combine);

    // ── Classification ────────────────────────────────────────────────────
    [Theory]
    [InlineData(typeof(int), GridColumnCategory.Numeric)]
    [InlineData(typeof(decimal), GridColumnCategory.Numeric)]
    [InlineData(typeof(double), GridColumnCategory.Numeric)]
    [InlineData(typeof(DateTime), GridColumnCategory.Temporal)]
    [InlineData(typeof(string), GridColumnCategory.Text)]
    [InlineData(typeof(bool), GridColumnCategory.Boolean)]
    [InlineData(typeof(byte[]), GridColumnCategory.Other)]
    [InlineData(typeof(int?), GridColumnCategory.Numeric)]
    public void Classify_MapsClrTypeToCategory(Type t, GridColumnCategory expected)
        => Assert.Equal(expected, GridColumnClassifier.Classify(t));

    [Fact]
    public void OperatorsFor_TextHasContains_NumericHasOrdering()
    {
        Assert.Contains(GridFilterOperator.Contains, GridColumnClassifier.OperatorsFor(GridColumnCategory.Text));
        Assert.DoesNotContain(GridFilterOperator.Contains, GridColumnClassifier.OperatorsFor(GridColumnCategory.Numeric));
        Assert.Contains(GridFilterOperator.GreaterThan, GridColumnClassifier.OperatorsFor(GridColumnCategory.Numeric));
        Assert.DoesNotContain(GridFilterOperator.GreaterThan, GridColumnClassifier.OperatorsFor(GridColumnCategory.Text));
    }

    [Fact]
    public void AggregatesFor_NumericHasSum_TextDoesNot()
    {
        Assert.Contains(GridAggregate.Sum, GridColumnClassifier.AggregatesFor(GridColumnCategory.Numeric));
        Assert.DoesNotContain(GridAggregate.Sum, GridColumnClassifier.AggregatesFor(GridColumnCategory.Text));
        Assert.Contains(GridAggregate.CountDistinct, GridColumnClassifier.AggregatesFor(GridColumnCategory.Text));
    }

    // ── Value conversion ──────────────────────────────────────────────────
    [Fact]
    public void TryConvert_Numeric_ParsesInteger()
    {
        Assert.True(GridValueConverter.TryConvert("1500", GridColumnCategory.Numeric, out var v));
        Assert.Equal(1500m, v);
    }

    [Fact]
    public void TryConvert_Numeric_RejectsNonNumeric()
        => Assert.False(GridValueConverter.TryConvert("abc", GridColumnCategory.Numeric, out _));

    [Fact]
    public void TryConvert_Boolean_AcceptsOneZero()
    {
        Assert.True(GridValueConverter.TryConvert("1", GridColumnCategory.Boolean, out var t));
        Assert.Equal(true, t);
        Assert.True(GridValueConverter.TryConvert("0", GridColumnCategory.Boolean, out var f));
        Assert.Equal(false, f);
    }

    // ── Filter evaluation ─────────────────────────────────────────────────
    [Fact]
    public void Matches_EmptyFilter_AlwaysTrue()
        => Assert.True(GridFilterEvaluator.Matches(new object?[] { 1m }, GridFilter.Empty, Cols));

    [Fact]
    public void Matches_NumericGreaterThan()
    {
        var f = One(0, "AMOUNT", GridFilterOperator.GreaterThan, "1000");
        Assert.True(GridFilterEvaluator.Matches(new object?[] { 1500m }, f, Cols));
        Assert.False(GridFilterEvaluator.Matches(new object?[] { 500m }, f, Cols));
    }

    [Fact]
    public void Matches_TextEquals_IsCaseSensitive()
    {
        var f = One(1, "NAME", GridFilterOperator.Equals, "ACME");
        Assert.True(GridFilterEvaluator.Matches(new object?[] { null, "ACME" }, f, Cols));
        Assert.False(GridFilterEvaluator.Matches(new object?[] { null, "acme" }, f, Cols));
    }

    [Fact]
    public void Matches_Contains_IsCaseInsensitive()
    {
        var f = One(1, "NAME", GridFilterOperator.Contains, "test");
        Assert.True(GridFilterEvaluator.Matches(new object?[] { null, "A TEST row" }, f, Cols));
        Assert.False(GridFilterEvaluator.Matches(new object?[] { null, "nothing" }, f, Cols));
    }

    [Fact]
    public void Matches_StartsWith_IsCaseSensitive()
    {
        var f = One(1, "NAME", GridFilterOperator.StartsWith, "AC");
        Assert.True(GridFilterEvaluator.Matches(new object?[] { null, "ACME" }, f, Cols));
        Assert.False(GridFilterEvaluator.Matches(new object?[] { null, "acme" }, f, Cols));
    }

    [Fact]
    public void Matches_EndsWith()
    {
        var f = One(1, "NAME", GridFilterOperator.EndsWith, "ME");
        Assert.True(GridFilterEvaluator.Matches(new object?[] { null, "ACME" }, f, Cols));
        Assert.False(GridFilterEvaluator.Matches(new object?[] { null, "ACMEx" }, f, Cols));
    }

    [Fact]
    public void Matches_IsNull_And_IsNotNull()
    {
        var isNull = One(1, "NAME", GridFilterOperator.IsNull, null);
        var notNull = One(1, "NAME", GridFilterOperator.IsNotNull, null);
        Assert.True(GridFilterEvaluator.Matches(new object?[] { null, null }, isNull, Cols));
        Assert.False(GridFilterEvaluator.Matches(new object?[] { null, "x" }, isNull, Cols));
        Assert.True(GridFilterEvaluator.Matches(new object?[] { null, "x" }, notNull, Cols));
    }

    [Fact]
    public void Matches_NullCell_FailsComparison()
    {
        var f = One(0, "AMOUNT", GridFilterOperator.Equals, "10");
        Assert.False(GridFilterEvaluator.Matches(new object?[] { null }, f, Cols));
    }

    [Fact]
    public void Matches_And_RequiresAll_Or_RequiresAny()
    {
        var and = new GridFilter(new[]
        {
            new GridFilterCondition(0, "AMOUNT", GridFilterOperator.GreaterThan, "100"),
            new GridFilterCondition(1, "NAME", GridFilterOperator.Equals, "ACME"),
        }, GridFilterCombine.And);
        Assert.True(GridFilterEvaluator.Matches(new object?[] { 500m, "ACME" }, and, Cols));
        Assert.False(GridFilterEvaluator.Matches(new object?[] { 500m, "OTHER" }, and, Cols));

        var or = new GridFilter(and.Conditions, GridFilterCombine.Or);
        Assert.True(GridFilterEvaluator.Matches(new object?[] { 5m, "ACME" }, or, Cols));
        Assert.True(GridFilterEvaluator.Matches(new object?[] { 500m, "OTHER" }, or, Cols));
        Assert.False(GridFilterEvaluator.Matches(new object?[] { 5m, "OTHER" }, or, Cols));
    }

    // ── Aggregation ───────────────────────────────────────────────────────
    private static IReadOnlyList<object?[]> Rows(params object?[][] r) => r;

    [Fact]
    public void Aggregate_Sum_Avg_Min_Max_Numeric()
    {
        var rows = Rows(new object?[] { 100m }, new object?[] { 200m }, new object?[] { 300m });
        Assert.Equal(600m, GridAggregator.Compute(rows, 0, GridAggregate.Sum, typeof(decimal)));
        Assert.Equal(200m, GridAggregator.Compute(rows, 0, GridAggregate.Avg, typeof(decimal)));
        Assert.Equal(100m, GridAggregator.Compute(rows, 0, GridAggregate.Min, typeof(decimal)));
        Assert.Equal(300m, GridAggregator.Compute(rows, 0, GridAggregate.Max, typeof(decimal)));
    }

    [Fact]
    public void Aggregate_Count_SkipsNulls_CountDistinct_Dedups()
    {
        var rows = Rows(new object?[] { "A" }, new object?[] { "A" }, new object?[] { null }, new object?[] { "B" });
        Assert.Equal(3L, GridAggregator.Compute(rows, 0, GridAggregate.Count, typeof(string)));
        Assert.Equal(2L, GridAggregator.Compute(rows, 0, GridAggregate.CountDistinct, typeof(string)));
    }

    [Fact]
    public void Aggregate_Temporal_MinMax()
    {
        var a = new DateTime(2020, 1, 1);
        var b = new DateTime(2021, 6, 15);
        var rows = Rows(new object?[] { b }, new object?[] { a });
        Assert.Equal(a, GridAggregator.Compute(rows, 0, GridAggregate.Min, typeof(DateTime)));
        Assert.Equal(b, GridAggregator.Compute(rows, 0, GridAggregate.Max, typeof(DateTime)));
    }

    [Fact]
    public void Aggregate_Sum_OnText_ReturnsNull()
    {
        var rows = Rows(new object?[] { "A" });
        Assert.Null(GridAggregator.Compute(rows, 0, GridAggregate.Sum, typeof(string)));
    }

    [Fact]
    public void Aggregate_Sum_EmptySet_ReturnsNull()
        => Assert.Null(GridAggregator.Compute(Array.Empty<object?[]>(), 0, GridAggregate.Sum, typeof(decimal)));
}
