using System.Collections.Generic;
using EmberTern.Core.Performance;

namespace EmberTern.App.ViewModels;

/// <summary>Read-only projection of a <see cref="Finding"/> for the Findings zone.
/// Phase 2 findings are measured-reads observations — no recommendation/action buttons.</summary>
public sealed class FindingViewModel
{
    public FindingViewModel(Finding finding) => Finding = finding;

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

    public string Title => Finding.Title;

    public string Explanation => Finding.Explanation;

    public IReadOnlyList<FindingEvidence> Evidence => Finding.Evidence;

    public bool HasEvidence => Finding.Evidence.Count > 0;
}
