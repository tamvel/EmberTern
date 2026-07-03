using System;
using System.Linq;
using EmberTern.Core.Performance;
using Xunit;

namespace EmberTern.Tests;

public class FindingGuidanceCatalogTests
{
    // Every finding kind the advisor actually emits (R1–R6) must carry guidance.
    [Theory]
    [InlineData(FindingKind.CostlyFullScan)]
    [InlineData(FindingKind.MissingIndexCandidate)]
    [InlineData(FindingKind.NonSargablePredicate)]
    [InlineData(FindingKind.LowSelectivityIndex)]
    [InlineData(FindingKind.StaleStatistics)]
    [InlineData(FindingKind.HighReadAmplification)]
    public void EveryEmittedKind_HasGuidance(FindingKind kind)
    {
        var g = FindingGuidanceCatalog.For(kind);
        Assert.True(g.HasItems);
        Assert.Equal("What to investigate", g.Heading);
        Assert.All(g.Items, item => Assert.False(string.IsNullOrWhiteSpace(item)));
    }

    [Fact]
    public void Guidance_UsesInvestigationLanguage_NotImperativeOrDdl()
    {
        // No prescriptive / action verbs anywhere in the guidance text.
        string[] banned = { "CREATE INDEX", "ALTER INDEX", "ADD INDEX", "MUST ", "EXECUTE", "FIX " };
        foreach (var kind in Enum.GetValues<FindingKind>())
        {
            foreach (var item in FindingGuidanceCatalog.For(kind).Items)
            {
                var upper = item.ToUpperInvariant();
                foreach (var b in banned)
                {
                    Assert.False(upper.Contains(b), $"'{item}' contains banned phrase '{b}'");
                }
            }
        }
    }

    [Fact]
    public void EachGuidance_IsConcise_TwoToThreeBullets()
    {
        foreach (var kind in new[]
        {
            FindingKind.CostlyFullScan, FindingKind.MissingIndexCandidate, FindingKind.NonSargablePredicate,
            FindingKind.LowSelectivityIndex, FindingKind.StaleStatistics, FindingKind.HighReadAmplification,
        })
        {
            int n = FindingGuidanceCatalog.For(kind).Items.Count;
            Assert.InRange(n, 2, 3);
        }
    }
}
