using System;
using System.Globalization;
using EmberTern.Licensing;

namespace EmberTern.App.Licensing;

/// <summary>
/// ⭐⭐ <b>The ONE place a licence verdict becomes the sentence a person reads.</b>
///
/// <para>⭐ <b>Every failure message answers three questions: what happened · why · what to do now.</b>
/// ⛔ Never <i>"License validation failed (code 7)."</i> A licence problem stops someone from working, and
/// the message is the entire support experience — design §17.3.</para>
///
/// <para>⚠⚠ <b>This is the exact shape of the Phase-5 charset defect and it will repeat if it is not
/// planned against.</b> There, a perfectly translated resource existed and a Polish user still read
/// English, because the value was wrapped on the way out and the display site read the wrong member.
/// Licensing is the same shape: a verdict produced deep in a pure library, surfaced by App. ⭐ The defence
/// is that the words are resolved HERE, at display time, from <see cref="UiStrings"/> — and that
/// <c>LicenseTextTests</c> resolves every enum value through this class in BOTH languages, so a missing or
/// unread entry fails the build rather than reaching a customer.</para>
///
/// <para>⭐ <b>Why the verdict travels as an enum rather than the project's ratified <c>MessageKey</c>
/// currency</b> (design §9.1): <c>MessageKey</c> presumes ONE resource catalog — App's. A licensing
/// verdict is rendered by two applications with two independent catalogs (EmberTern and the License
/// Manager), so a key string would have to resolve in both, and a key present in one and missing in the
/// other fails silently. An enum is a closed set each application maps on its own terms, and the compiler
/// can see every value.</para>
/// </summary>
internal static class LicenseText
{
    /// <summary>The headline for a verdict — what state the licence is in.</summary>
    internal static string Headline(LicenseVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return verdict.Status switch
        {
            LicenseStatus.Valid => UiStrings.LicenseStatusValid,
            LicenseStatus.Grace => UiStrings.LicenseStatusGrace,
            LicenseStatus.Expired => UiStrings.LicenseStatusExpired,
            LicenseStatus.NotYetValid => UiStrings.LicenseStatusNotYetValid,
            LicenseStatus.VersionNotCovered => UiStrings.LicenseStatusVersionNotCovered,
            LicenseStatus.Unlicensed => UiStrings.LicenseStatusUnlicensed,
            _ => UiStrings.LicenseStatusInvalid,
        };
    }

    /// <summary>
    /// The full sentence for a verdict: what happened, why, and what to do now.
    ///
    /// <para>⚠ Dynamic values — a date, the licensee's name — travel as ARGUMENTS into a whole sentence
    /// from the catalog. ⛔ Never a fragment concatenated in code: word order is the translator's decision,
    /// not English's (Architecture rule 12).</para>
    /// </summary>
    internal static string Explain(LicenseVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return verdict.Status switch
        {
            LicenseStatus.Valid when verdict.Payload is { } valid =>
                Format(UiStrings.LicenseExplainValid, valid.Licensee, Date(valid.ExpiresAt)),

            LicenseStatus.Grace when verdict.Payload is { } grace =>
                Format(UiStrings.LicenseExplainGrace, Date(grace.ExpiresAt)),

            LicenseStatus.Expired when verdict.Payload is { } expired =>
                Format(UiStrings.LicenseExplainExpired, Date(expired.ExpiresAt)),

            LicenseStatus.NotYetValid when verdict.Payload is { } future =>
                Format(UiStrings.LicenseExplainNotYetValid, Date(future.NotBefore)),

            LicenseStatus.VersionNotCovered when verdict.Payload is { MaintenanceUntil: { } until } =>
                Format(UiStrings.LicenseExplainVersionNotCovered, Date(until)),

            LicenseStatus.Unlicensed => UiStrings.LicenseExplainUnlicensed,

            _ => ExplainFailure(verdict.Failure),
        };
    }

    /// <summary>
    /// Why an artifact could not be accepted.
    ///
    /// <para>⭐ The distinction the user actually needs is <b>"this is not the right kind of file"</b> versus
    /// <b>"this file has been altered"</b> versus <b>"this build cannot read it"</b> — three different things
    /// to do next. The eleven enum values collapse onto those three answers plus the product mismatch,
    /// because eleven separate sentences would be eleven ways of saying the same three.</para>
    /// </summary>
    internal static string ExplainFailure(LicenseFailure failure) => failure switch
    {
        LicenseFailure.FileMissing => UiStrings.LicenseExplainUnlicensed,

        // Not a licence at all, or damaged in transit — a wrong attachment, a truncated copy-paste.
        LicenseFailure.NotALicense or
        LicenseFailure.MalformedArmor or
        LicenseFailure.MalformedEnvelope or
        LicenseFailure.MalformedPayload => UiStrings.LicenseExplainNotALicense,

        // Authentic in shape, but this build cannot judge it.
        LicenseFailure.UnsupportedVersion => UiStrings.LicenseExplainUnsupportedVersion,
        LicenseFailure.UnknownKey or
        LicenseFailure.RevokedKey or
        LicenseFailure.AlgorithmMismatch => UiStrings.LicenseExplainUnknownKey,

        // The bytes do not match the signature.
        LicenseFailure.SignatureInvalid => UiStrings.LicenseExplainSignatureInvalid,

        LicenseFailure.WrongProduct => UiStrings.LicenseExplainWrongProduct,

        _ => UiStrings.LicenseExplainNotALicense,
    };

    /// <summary>
    /// The technical token for <c>[Copy details]</c> — an unknown <c>kid</c>, a parse offset.
    ///
    /// <para>⛔ Never rendered as prose (§9.1). It exists so a customer can paste something exact into an
    /// e-mail, and it is deliberately not translated: it is a token for us, not a sentence for them.</para>
    /// </summary>
    internal static string Details(LicenseVerdict verdict, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        var detail = string.IsNullOrWhiteSpace(verdict.Detail) ? "-" : verdict.Detail;
        var path = string.IsNullOrWhiteSpace(sourcePath) ? "-" : sourcePath;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"status={verdict.Status} failure={verdict.Failure} detail={detail} file={path} version={AppInfo.Version}");
    }

    /// <summary>A date as the user's culture writes it — never a raw round-trip stamp.</summary>
    private static string Date(DateTimeOffset value) =>
        value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    private static string Format(string template, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, template, arguments);
}
