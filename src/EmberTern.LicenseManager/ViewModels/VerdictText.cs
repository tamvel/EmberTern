using System.Globalization;
using EmberTern.Licensing;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// Turns a <see cref="LicenseVerdict"/> into the sentence the operator reads — in ONE place.
///
/// <para>⭐⭐ Extracted at L5.2, when the artifact detail pane became a second consumer of a mapping that
/// until then lived inside <c>InspectLatest</c>. Two switches over the same enum is how a message strip
/// and a detail panel end up describing one artifact two different ways, and the operator has no way to
/// tell which of them is the application's actual opinion.</para>
///
/// <para>⛔ It DESCRIBES a verdict; it never produces one. The verdict comes from
/// <see cref="LicenseVerifier"/> and from nothing else — this class must never grow a date comparison
/// (§41.4: nothing in the UI invents a licensing state).</para>
/// </summary>
internal static class VerdictText
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>The verdict as a severity and a sentence.</summary>
    /// <remarks>
    /// ⚠ The expiry date is rendered INVARIANTLY here and handed over as a finished string, never as a
    /// <c>DateOnly</c>. <c>Loc.Format</c> formats its arguments under the reader's culture, so passing the
    /// value itself would let the interface language decide how a licence's date reads — and the ISO form
    /// is a technical contract (`terminology.md` §4.4), pinned by <c>DatePresentationTests</c>.
    /// </remarks>
    internal static StatusMessage Describe(LicenseVerdict verdict) => verdict.Status switch
    {
        LicenseStatus.Valid => StatusMessage.Success(
            StatusCatalog.VerdictValid,
            verdict.Payload!.ExpiresAt.ToString(DateFormat, CultureInfo.InvariantCulture),
            verdict.Payload.Licensee),
        LicenseStatus.Grace => StatusMessage.Warning(StatusCatalog.VerdictGrace),
        LicenseStatus.Expired => StatusMessage.Warning(StatusCatalog.VerdictExpired),
        LicenseStatus.NotYetValid => StatusMessage.Info(StatusCatalog.VerdictNotYetValid),
        _ => StatusMessage.Error(StatusCatalog.VerdictRefused, verdict.Failure),
    };
}
