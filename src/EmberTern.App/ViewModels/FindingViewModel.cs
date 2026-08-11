using System.Collections.Generic;
using System.Linq;
using EmberTern.App.Localization;
using EmberTern.Core.Performance;

namespace EmberTern.App.ViewModels;

/// <summary>Read-only projection of a <see cref="Finding"/> for the Findings zone. Phase 3a
/// findings are measured-first advisor observations with a confidence level — investigation-
/// oriented, no recommendation/action buttons.
///
/// <para>⭐ <b>Etap C7 — this is where Core's keys become words.</b> The rule hands up a
/// <c>LocalizableMessage</c>; this class resolves it with <c>Loc.Format</c> at READ time, exactly as
/// <c>SessionWarningViewModel</c> has done for SessionHealth's findings since C1. ⛔ Nothing here caches a
/// resolved string in a field — a captured string is the one shape that cannot follow a language change
/// (guarded by <c>NoField_CapturesALocalizedString</c>), and the XAML binds these properties by name.</para></summary>
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
        FindingSeverity.High => UiStrings.FindingSeverityHigh,
        FindingSeverity.Medium => UiStrings.FindingSeverityMedium,
        FindingSeverity.Low => UiStrings.FindingSeverityLow,
        _ => UiStrings.FindingSeverityInfo,
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

    public string Title => Loc.Format(Finding.Title);

    public string Explanation => Finding.Explanation is { } e ? Loc.Format(e) : string.Empty;

    /// <summary>The evidence rows with their LABELS resolved; each value is Core's verbatim datum.
    /// <para>⚠ Projected into a view row rather than handed through as <c>FindingEvidence</c>, because the
    /// label is now a key and the view binds <c>Label</c> as text. ⭐ The XAML is unchanged: the template
    /// still binds <c>Label</c> and <c>Value</c>, and this row supplies both under the same names.</para></summary>
    public IReadOnlyList<FindingEvidenceViewModel> Evidence =>
        Finding.Evidence.Select(e => new FindingEvidenceViewModel(e)).ToList();

    public bool HasEvidence => Finding.Evidence.Count > 0;

    // Phase 4 — investigation guidance (derived from the finding's kind; inherits its confidence).
    public string GuidanceHeading => Loc.Text(_guidance.Heading);

    public IReadOnlyList<string> GuidanceItems => _guidance.Items.Select(Loc.Text).ToList();

    public bool HasGuidance => _guidance.HasItems;

    // Phase 5 — recommendation ("potential improvement"; inherits the finding's confidence).
    public string RecommendationHeading => Loc.Text(_recommendation.Heading);

    public string RecommendationText =>
        _recommendation.Text is { } t ? Loc.Format(t) : string.Empty;

    public bool HasRecommendation => _recommendation.HasText;
}

/// <summary>One resolved "Label: Value" evidence row.
/// <para>⭐ The split C7 made structural: <see cref="Label"/> is EmberTern's word, resolved here;
/// <see cref="Value"/> is Core's measured datum, passed through untouched.</para></summary>
public sealed class FindingEvidenceViewModel
{
    private readonly FindingEvidence _evidence;

    public FindingEvidenceViewModel(FindingEvidence evidence) => _evidence = evidence;

    public string Label => Loc.Text(_evidence.Label);

    public string Value => _evidence.Value;
}
