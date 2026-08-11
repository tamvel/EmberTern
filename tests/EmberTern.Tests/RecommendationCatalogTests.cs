using System;
using EmberTern.App.Localization;
using EmberTern.App.ViewModels;
using EmberTern.Core.Localization;
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
        Title = LocalizableMessage.Of(PerfMessages.MissingIndexTitle, "T", column ?? "COL"),
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
        Assert.Equal("Potential improvement", Loc.Text(r.Heading));
    }

    [Fact]
    public void MissingIndex_NamesTheColumn_WhenKnown()
    {
        var r = RecommendationCatalog.For(F(FindingKind.MissingIndexCandidate, column: "CUSTOMER_ID"));
        // ⭐ The column is the message's DATUM, so assert it there as well as in the rendered sentence.
        Assert.Equal(PerfMessages.MissingIndexRecommendationOnColumn, r.Text!.Key);
        Assert.Contains("CUSTOMER_ID", r.Text.Arguments);
        Assert.Contains("CUSTOMER_ID", Loc.Format(r.Text));
    }

    /// <summary>
    /// ⭐⭐ <b>D‑6: the fallback is a WHOLE SENTENCE, not a noun dropped into one.</b> The producer used to
    /// substitute <c>"the filtered column"</c> as an argument — English-only by construction, because an
    /// argument cannot know which grammatical case the surrounding sentence needs. The two cases are now two
    /// keys, and the assertion is on that split, not only on the words it happens to render to today.
    /// </summary>
    [Fact]
    public void MissingIndex_FallsBackWhenColumnUnknown()
    {
        var r = RecommendationCatalog.For(F(FindingKind.MissingIndexCandidate, column: null));

        Assert.Equal(PerfMessages.MissingIndexRecommendationOnFilteredColumn, r.Text!.Key);
        Assert.Empty(r.Text.Arguments);   // ⛔ no noun travels as data

        Assert.Contains("the filtered column", Loc.Format(r.Text));
        Assert.DoesNotContain("CUSTOMER_ID", Loc.Format(r.Text));
    }

    [Fact]
    public void Recommendations_UseReviewLanguage_NotPrescriptiveOrDdl()
    {
        string[] banned = { "MUST", "REQUIRED", "GUARANTEED", "FIX", "CREATE INDEX", "ADD INDEX", "EXECUTE" };
        foreach (var kind in Enum.GetValues<FindingKind>())
        {
            var recommendation = RecommendationCatalog.For(F(kind, "COL"));
            if (recommendation.Text is not { } message)
            {
                continue;
            }
            var text = Loc.Format(message).ToUpperInvariant();
            foreach (var b in banned)
            {
                Assert.False(text.Contains(b), $"recommendation for {kind} contains banned phrase '{b}'");
            }
        }
    }
}
