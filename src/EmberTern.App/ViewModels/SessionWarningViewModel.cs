using System.Collections.Generic;
using System.Linq;
using EmberTern.App.Localization;
using EmberTern.Core.Diagnostics;

namespace EmberTern.App.ViewModels;

/// <summary>Read-only projection of a <see cref="SessionHealthFinding"/> for the Warnings tab —
/// same card shape as the Performance <c>FindingViewModel</c> (severity + evidence + investigation
/// text). No confidence label in V1 (deferred).
///
/// <para>⭐ <b>This is where a Core message becomes words (decision D‑3).</b> The finding carries
/// <c>LocalizableMessage</c>es; every text member below resolves one through <see cref="Loc.Format"/> <i>on
/// read</i>. ⛔ Do not cache the resolved text in a field: a field would be filled in whatever language was
/// current when the analysis ran, and would then render the old language after a switch — the exact failure
/// mode the localization stage measured and designed against (<c>static readonly</c> freezes, a property does
/// not).</para>
/// </summary>
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

    public string Title => Loc.Format(_f.Title);
    public string Explanation => Loc.Format(_f.Explanation);

    public string Impact => _f.Impact is { } m ? Loc.Format(m) : string.Empty;
    public bool HasImpact => _f.Impact is not null;

    // ⚠ Resolved into a NEW list on each read, because the XAML binds each item with `Text="{Binding}"` — the
    // bound object IS the string, so there is no property on it to re-read. The list is rebuilt when the card
    // is rebuilt, which is what SessionManagerTabViewModel does on a language change.
    public IReadOnlyList<string> Evidence => _f.Evidence.Select(Loc.Format).ToList();
    public bool HasEvidence => _f.Evidence.Count > 0;

    public IReadOnlyList<string> WhatToCheck => _f.WhatToCheck.Select(Loc.Format).ToList();
    public bool HasWhatToCheck => _f.WhatToCheck.Count > 0;
}
