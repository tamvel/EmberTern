using System;
using EmberTern.App.ViewModels;
using EmberTern.Core.Performance;
using Xunit;

namespace EmberTern.Tests;

public class RecommendationCatalogTests
{
    [Fact]
    public void FindingViewModel_ExposesRecommendation()
    {
        var vm = new FindingViewModel(F(FindingKind.MissingIndexCandidate, column: "CUSTOMER_ID"));
        Assert.True(vm.HasRecommendation);
        Assert.Equal("Potential improvement", vm.RecommendationHeading);
        Assert.Contains("CUSTOMER_ID", vm.RecommendationText);
    }

    private static Finding F(FindingKind kind, string? column = null) => new()
    {
        Kind = kind,
        Severity = FindingSeverity.Medium,
        Title = "x",
        Column = column,
    };

    [Theory]
    [InlineData(FindingKind.CostlyFullScan)]
    [InlineData(FindingKind.MissingIndexCandidate)]
    [InlineData(FindingKind.NonSargablePredicate)]
    [InlineData(FindingKind.LowSelectivityIndex)]
    [InlineData(FindingKind.StaleStatistics)]
    [InlineData(FindingKind.HighReadAmplification)]
    public void EveryEmittedKind_HasRecommendation(FindingKind kind)
    {
        var r = RecommendationCatalog.For(F(kind));
        Assert.True(r.HasText);
        Assert.Equal("Potential improvement", r.Heading);
    }

    [Fact]
    public void MissingIndex_NamesTheColumn_WhenKnown()
    {
        var r = RecommendationCatalog.For(F(FindingKind.MissingIndexCandidate, column: "CUSTOMER_ID"));
        Assert.Contains("CUSTOMER_ID", r.Text);
    }

    [Fact]
    public void MissingIndex_FallsBackWhenColumnUnknown()
    {
        var r = RecommendationCatalog.For(F(FindingKind.MissingIndexCandidate, column: null));
        Assert.Contains("the filtered column", r.Text);
        Assert.DoesNotContain("CUSTOMER_ID", r.Text);
    }

    [Fact]
    public void Recommendations_UseReviewLanguage_NotPrescriptiveOrDdl()
    {
        string[] banned = { "MUST", "REQUIRED", "GUARANTEED", "FIX", "CREATE INDEX", "ADD INDEX", "EXECUTE" };
        foreach (var kind in Enum.GetValues<FindingKind>())
        {
            var text = RecommendationCatalog.For(F(kind, "COL")).Text.ToUpperInvariant();
            foreach (var b in banned)
            {
                Assert.False(text.Contains(b), $"recommendation for {kind} contains banned phrase '{b}'");
            }
        }
    }
}
