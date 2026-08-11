using System;
using System.Linq;
using EmberTern.App.Localization;
using EmberTern.App.ViewModels;
using EmberTern.Core.Localization;
using EmberTern.Core.Performance;
using Xunit;

namespace EmberTern.Tests;

public class FindingGuidanceCatalogTests
{
    // ⚠ C7: a finding's title is a LocalizableMessage now. The fixture uses a real key with real arguments so
    // that anything resolving it renders a sentence rather than throwing on a missing placeholder.
    private static LocalizableMessage AnyTitle =>
        LocalizableMessage.Of(PerfMessages.MissingIndexTitle, "T", "COL");

    [Fact]
    public void FindingViewModel_ExposesGuidanceForItsKind()
    {
        var vm = new FindingViewModel(new Finding
        {
            Kind = FindingKind.CostlyFullScan,
            Severity = FindingSeverity.High,
            Title = AnyTitle,
        });
        Assert.True(vm.HasGuidance);
        Assert.Equal("What to investigate", vm.GuidanceHeading);
        Assert.NotEmpty(vm.GuidanceItems);
    }

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
        Assert.Equal("What to investigate", Loc.Text(g.Heading));
        // ⭐ Resolving each item is what makes this an assertion about the SCREEN. A key that resolves to
        // itself (no catalog entry) would be a non-empty string too — so the resolution must not be the key.
        Assert.All(g.Items, item =>
        {
            var text = Loc.Text(item);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.NotEqual(item.Value, text);
        });
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
                var text = Loc.Text(item);
                var upper = text.ToUpperInvariant();
                foreach (var b in banned)
                {
                    Assert.False(upper.Contains(b), $"'{text}' contains banned phrase '{b}'");
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
