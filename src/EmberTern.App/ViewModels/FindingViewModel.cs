using System.Collections.Generic;
using EmberTern.Core.Performance;

namespace EmberTern.App.ViewModels;

/// <summary>Read-only projection of a <see cref="Finding"/> for the Findings zone. Phase 3a
/// findings are measured-first advisor observations with a confidence level — investigation-
/// oriented, no recommendation/action buttons.</summary>
public sealed class FindingViewModel
{
    private readonly FindingGuidance _guidance;
    private readonly Recommendation _recommendation;

    public FindingViewModel(Finding finding)
    {
        Finding = finding;
        _guidance = FindingGuidanceCatalog.For(finding.Kind);
        _recommendation = RecommendationCatalog.For(finding);
    }

    public Finding Finding { get; }

    public FindingSeverity Severity => Finding.Severity;

    public string SeverityText => Finding.Severity switch
    {
        FindingSeverity.High => "HIGH",
        FindingSeverity.Medium => "MED",
        FindingSeverity.Low => "LOW",
        _ => "INFO",
    };

    /// <summary>Theme brush key for the severity dot/label, resolved via IconBrushConverter.</summary>
    public string SeverityBrushKey => Finding.Severity switch
    {
        FindingSeverity.High => "DangerIconBrush",
        FindingSeverity.Medium => "WarningBrush",
        FindingSeverity.Low => "InfoIconBrush",
        _ => "SubtleForegroundBrush",
    };

    /// <summary>"High/Medium/Low confidence" — surfaced so a questionable finding reads as such.</summary>
    public string ConfidenceText => Finding.Confidence switch
    {
        FindingConfidence.High => UiStrings.FindingConfidenceHigh,
        FindingConfidence.Medium => UiStrings.FindingConfidenceMedium,
        _ => UiStrings.FindingConfidenceLow,
    };

    public string Title => Finding.Title;

    public string Explanation => Finding.Explanation;

    public IReadOnlyList<FindingEvidence> Evidence => Finding.Evidence;

    public bool HasEvidence => Finding.Evidence.Count > 0;

    // Phase 4 — investigation guidance (derived from the finding's kind; inherits its confidence).
    public string GuidanceHeading => _guidance.Heading;

    public IReadOnlyList<string> GuidanceItems => _guidance.Items;

    public bool HasGuidance => _guidance.HasItems;

    // Phase 5 — recommendation ("potential improvement"; inherits the finding's confidence).
    public string RecommendationHeading => _recommendation.Heading;

    public string RecommendationText => _recommendation.Text;

    public bool HasRecommendation => _recommendation.HasText;
}
