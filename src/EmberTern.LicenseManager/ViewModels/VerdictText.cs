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
    internal static StatusMessage Describe(LicenseVerdict verdict) => verdict.Status switch
    {
        LicenseStatus.Valid => StatusMessage.Success(
            "EmberTern would accept it: valid until " +
            $"{verdict.Payload!.ExpiresAt.ToString(DateFormat, CultureInfo.InvariantCulture)}, " +
            $"licensed to {verdict.Payload.Licensee}."),
        LicenseStatus.Grace => StatusMessage.Warning(
            "EmberTern would accept it, but it is past its expiry and inside the grace period."),
        LicenseStatus.Expired => StatusMessage.Warning("EmberTern would report it as expired."),
        LicenseStatus.NotYetValid => StatusMessage.Info("EmberTern would report it as not yet valid."),
        _ => StatusMessage.Error($"EmberTern would refuse it ({verdict.Failure})."),
    };
}
