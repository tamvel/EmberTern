using System;
using System.Globalization;
using EmberTern.App.Controls;
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
    /// ⭐⭐ <b>The sentence a user reads when a database connection is refused — ONE short sentence, chosen by
    /// state.</b>
    ///
    /// <para>⚠⚠ <b>Shortened after the user saw L4b running (2026-08-15), and the reason is worth keeping.</b>
    /// This used to return <see cref="Explain"/> plus a second sentence repeating what to do — about 250
    /// characters, landing in the STATUS BAR. It ellipsised, it read as a technical dump stretched across the
    /// window, and it repeated word for word what the banner above it and the activation window were already
    /// saying. ⭐ <b>Each surface now has one job:</b> the status bar says WHAT IS BLOCKED, the banner and the
    /// activation window say WHAT TO DO. ⛔ Do not re-compose this from <see cref="Explain"/>.</para>
    ///
    /// <para>⚠ One sentence per state rather than one generic line: an expired licence and a licence this
    /// build cannot read call for different actions, and a single sentence covering both would say neither.
    /// The switch mirrors <see cref="Headline"/>'s exactly, including the <c>_</c> arm — an unusable verdict
    /// that is none of the named states is, for the user, a licence that cannot be read.</para>
    ///
    /// <para>⚠⚠ <b>This is the member every refusal site must call, and the reason is the Phase-5 defect</b>
    /// (design §17.3): <see cref="LicenseBlockedException"/> carries the VERDICT precisely so that no display
    /// site is tempted to render <c>ex.Message</c>, which would be an untranslated developer breadcrumb.
    /// <c>LicenseSurfaceLocalizationTests</c> resolves this in both languages, and measures that it fits the
    /// status bar with the engine the product lays out with.</para>
    /// </summary>
    internal static string ConnectionRefused(LicenseVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return verdict.Status switch
        {
            LicenseStatus.Expired => UiStrings.LicenseRefusedExpired,
            LicenseStatus.NotYetValid => UiStrings.LicenseRefusedNotYetValid,
            LicenseStatus.VersionNotCovered => UiStrings.LicenseRefusedVersionNotCovered,
            LicenseStatus.Unlicensed => UiStrings.LicenseRefusedUnlicensed,
            _ => UiStrings.LicenseRefusedInvalid,
        };
    }

    /// <summary>
    /// ⭐ <b>The ONE mapping from a licence verdict to the tone it is shown in.</b>
    ///
    /// <para>Read straight off design §7: a valid licence within 30 days of expiry is <c>Info</c> and
    /// dismissible, the grace period is a persistent <c>Warning</c>, and an expired or unreadable licence is
    /// an <c>Error</c>. ⚠ It lives here rather than in each host because two hosts choosing their own tone is
    /// how the same state comes to look routine in one place and alarming in another.</para>
    /// </summary>
    /// <param name="expiringSoon">Whether a still-valid licence is inside the expiry warning window.</param>
    internal static MessageSeverity SeverityOf(LicenseVerdict verdict, bool expiringSoon)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return verdict.Status switch
        {
            LicenseStatus.Valid => expiringSoon ? MessageSeverity.Info : MessageSeverity.Success,
            LicenseStatus.Grace => MessageSeverity.Warning,
            LicenseStatus.NotYetValid => MessageSeverity.Warning,
            LicenseStatus.Unlicensed => MessageSeverity.Warning,
            _ => MessageSeverity.Error,
        };
    }

    /// <summary>A date as the user's culture writes it — the one date format every licence surface uses.</summary>
    internal static string Day(DateTimeOffset value) => Date(value);

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
