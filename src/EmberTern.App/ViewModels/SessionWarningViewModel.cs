using System.Collections.Generic;
using EmberTern.Core.Diagnostics;

namespace EmberTern.App.ViewModels;

/// <summary>Read-only projection of a <see cref="SessionHealthFinding"/> for the Warnings tab —
/// same card shape as the Performance <c>FindingViewModel</c> (severity + evidence + investigation
/// text). No confidence label in V1 (deferred).</summary>
public sealed class SessionWarningViewModel
{
    private readonly SessionHealthFinding _f;

    public SessionWarningViewModel(SessionHealthFinding finding)
    {
        _f = finding;
    }

    public SessionHealthFinding Finding => _f;

    public long AttachmentId => _f.AttachmentId;
    public long? TransactionId => _f.TransactionId;

    /// <summary>Theme brush key for the severity icon, resolved via IconBrushConverter.</summary>
    public string SeverityBrushKey => _f.Severity == SessionHealthSeverity.Critical
        ? "DangerIconBrush"
        : "WarningBrush";

    /// <summary>Warning-triangle glyph (shared with the Exception object icon).</summary>
    public string SeverityGeometryKey => "Icon.Exception";

    public string Title => _f.Title;
    public string Explanation => _f.Explanation;

    public string Impact => _f.Impact;
    public bool HasImpact => !string.IsNullOrEmpty(_f.Impact);

    public IReadOnlyList<string> Evidence => _f.Evidence;
    public bool HasEvidence => _f.Evidence.Count > 0;

    public IReadOnlyList<string> WhatToCheck => _f.WhatToCheck;
    public bool HasWhatToCheck => _f.WhatToCheck.Count > 0;
}
