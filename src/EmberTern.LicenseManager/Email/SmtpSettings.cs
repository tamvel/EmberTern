using System;
using System.Collections.Generic;
using System.Globalization;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.ViewModels;

namespace EmberTern.LicenseManager.Email;

/// <summary>How the connection is secured.</summary>
public enum SmtpSecurity
{
    /// <summary>⭐ Upgrade the connection with <c>STARTTLS</c> before authenticating. The default.</summary>
    StartTls = 0,

    /// <summary>
    /// No transport security at all.
    ///
    /// <para>⚠ Offered for an internal relay that accepts unauthenticated submission on a trusted
    /// network, and for nothing else. ⛔ <see cref="SmtpSettings.Validate"/> refuses to combine it with a
    /// username: sending a password in the clear is not a choice this application lets an operator make
    /// by accident.</para>
    /// </summary>
    None = 1,
}

/// <summary>
/// Where the License Manager sends from.
///
/// <para>⭐⭐ <b>Every one of these is DATA, never a constant.</b> The L6 measurement was taken on a Gmail
/// test mailbox and the production mailbox is a company one that has not been measured yet — so moving to
/// it must be four fields typed into a window, not a code change. ⛔ No host, port or provider name
/// appears anywhere in this application's code.</para>
///
/// <para>⚠⚠ <b>Implicit TLS (port 465) is deliberately NOT offered</b>, and this is the one place to
/// record why: <see cref="System.Net.Mail.SmtpClient"/> cannot do it — it implements <c>STARTTLS</c> only,
/// and its <c>EnableSsl</c> means "upgrade", not "connect encrypted". Offering the mode would be offering
/// the operator a decision that can have no effect, which is exactly the defect that removed
/// <c>ClientLibraryPath</c> from EmberTern's connection dialog. ⭐ If a mailbox ever requires it, that is
/// a new sender implementation behind the L6.3 sender contract, not a new enum value.</para>
///
/// <para>⚠ The password is NOT on this record by accident — see <see cref="Password"/>.</para>
/// </summary>
public sealed record SmtpSettings
{
    /// <summary>The submission port nearly every provider uses for <c>STARTTLS</c>.</summary>
    public const int DefaultPort = 587;

    // ══ BULK SENDING (L10.1) ═══════════════════════════════════════════════════════════════════════
    // ⭐⭐ These two are a RATE LIMITER and an operator's window to notice something wrong — ⛔ never a
    //    promise about deliverability. What actually decides whether a mailbox accepts a run is SPF /
    //    DKIM / DMARC on the sender's domain, the IP's reputation and whether recipients mark it as
    //    spam, and none of that is in this application. §60.9 says so in the design; it is repeated
    //    here because this is where somebody would come looking for the guarantee.
    //
    // ⚠ They live on the SMTP settings because they are properties of DELIVERY, not preferences about
    //   the interface — which also keeps the configuration to one file, exactly as MessageLanguage did.

    /// <summary>
    /// How long to wait between two messages of a bulk run, in seconds.
    /// </summary>
    /// <remarks>
    /// 🔒 15 by the user's decision (§60.9). Chosen for two jobs at once: it keeps a run far below any
    /// per-minute limit, and it leaves a real window in which an operator can read the progress line and
    /// stop the run. ⚠ At this value a 38-message run takes about nine minutes, which is the honest cost.
    /// </remarks>
    public const int DefaultBulkDelaySeconds = 15;

    /// <summary>The shortest delay the operator may configure. ⚠ Not zero — a run with no pacing at all is the thing this exists to prevent.</summary>
    public const int MinBulkDelaySeconds = 1;

    /// <summary>The longest delay the operator may configure — ten minutes between messages.</summary>
    public const int MaxBulkDelaySeconds = 600;

    /// <summary>
    /// The most messages one bulk run may attempt.
    /// </summary>
    /// <remarks>
    /// 🔒 50 by the user's decision (§60.9). ⛔ Exceeding it makes the action UNAVAILABLE rather than
    /// warning about it, and raising it means going into this window — a deliberate step, which is the
    /// point: the limit exists so that "tick 200 licences and click once" cannot happen by accident.
    /// </remarks>
    public const int DefaultBulkMaxPerRun = 50;

    /// <summary>The smallest run limit that still permits a run.</summary>
    public const int MinBulkMaxPerRun = 1;

    /// <summary>The largest run limit the operator may configure.</summary>
    public const int MaxBulkMaxPerRun = 500;

    /// <summary>Nothing configured yet — what the settings window opens on before a first save.</summary>
    public static SmtpSettings Empty { get; } = new();

    /// <summary>The submission server, e.g. <c>smtp.gmail.com</c>.</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>The submission port.</summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>How the connection is secured.</summary>
    public SmtpSecurity Security { get; init; } = SmtpSecurity.StartTls;

    /// <summary>The address the customer sees, and the envelope sender.</summary>
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>The display name beside it. Optional — an address alone is a valid sender.</summary>
    public string FromName { get; init; } = string.Empty;

    /// <summary>
    /// The account that authenticates. ⭐ Kept separate from <see cref="FromAddress"/> on purpose: a relay
    /// commonly authenticates as one identity and sends as another, and collapsing the two would make
    /// that arrangement unreachable.
    /// </summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// The password, <b>in memory only</b>.
    ///
    /// <para>⭐ <b>Plaintext in memory, ciphertext at rest</b> — the arrangement EmberTern's
    /// <c>SecretProtector</c> established and the reason it is safe to keep the secret on the same record
    /// as the rest: a caller cannot forget to protect it, because the only thing that ever writes this
    /// record to disk is <see cref="SmtpSettingsStore"/>, and that is where the protection lives.
    /// ⛔ Never log it, never put it in a message, never write it anywhere but through the store.</para>
    ///
    /// <para>⚠ For a Gmail mailbox this is an <b>app password</b>, not the account password — measured
    /// 2026-08-19: Google refuses ordinary account passwords on SMTP.</para>
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// The language the licence e-mail is written in — <see cref="MessageLanguages"/>.
    ///
    /// <para>⭐⭐ <b>Independent of the application's interface language, deliberately.</b> The operator may
    /// work in one language while the customer reads another, and both directions must be possible. ⛔ Do
    /// not derive one from the other when the application gains its own localization (L8).</para>
    ///
    /// <para>⚠ It lives on the SMTP settings because it is a property of the message being delivered, and
    /// because that keeps L6 to one settings file. An application-wide language, when it exists, belongs
    /// somewhere else — it is not an e-mail setting.</para>
    /// </summary>
    public string MessageLanguage { get; init; } = MessageLanguages.Default;

    /// <summary>
    /// Seconds to wait between two messages of a bulk run. ⭐ See <see cref="DefaultBulkDelaySeconds"/>
    /// for what this does and does not promise.
    /// </summary>
    public int BulkDelaySeconds { get; init; } = DefaultBulkDelaySeconds;

    /// <summary>
    /// The most messages one bulk run may attempt. ⛔ A selection above it disables the action rather
    /// than warning about it.
    /// </summary>
    public int BulkMaxPerRun { get; init; } = DefaultBulkMaxPerRun;

    /// <summary>True when a host and a sender address are present — the minimum an <c>.eml</c> needs.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(FromAddress);

    /// <summary>True when this can also reach a server — everything above, plus a host.</summary>
    public bool CanSendDirectly =>
        IsConfigured && !string.IsNullOrWhiteSpace(Host);

    /// <summary>
    /// Everything wrong with these settings, in the order an operator would fix it. Empty means usable.
    ///
    /// <para>⭐ It answers about the settings ALONE. Whether the server accepts them is a question only
    /// the server can answer, and this deliberately does not pretend otherwise.</para>
    /// </summary>
    public IReadOnlyList<LocalizedText> Validate()
    {
        var problems = new List<LocalizedText>();

        // ⚠ Checked, but never REPAIRED here: an unrecognised code resolves to the default at the moment a
        //   message is composed (MessageLanguages.Resolve). A settings file is allowed to carry a language
        //   this build does not know — refusing to save because of it would fail the whole configuration
        //   over a preference that has a safe answer.
        if (!MessageLanguages.IsSupported(MessageLanguage))
        {
            problems.Add(new LocalizedText(StatusCatalog.SmtpUnknownMessageLanguage, MessageLanguage));
        }

        // ⚠⚠ CHECKED UNCONDITIONALLY, unlike Port — and the difference is deliberate rather than an
        //    oversight. The port is only meaningful once a host exists, so it is judged inside that
        //    branch; these two are properties of the SETTINGS themselves and a nonsense value is a fault
        //    whether or not a server has been typed yet. ⭐ They are also the two values a future bulk run
        //    reads without asking a question first, so "stored out of range" must be unreachable.
        // ⭐ REFUSED, never clamped. That is this window's established behaviour — `Save` reports every
        //   problem and writes nothing — and it is worth stating because §60.7 described it as clamping,
        //   which measurement did not support: `PortText` parses and reports, it does not repair. One
        //   numeric field that silently rewrites what the operator typed while another refuses would be
        //   two behaviours in one form.
        if (BulkDelaySeconds is < MinBulkDelaySeconds or > MaxBulkDelaySeconds)
        {
            problems.Add(new LocalizedText(
                StatusCatalog.SmtpBulkDelayOutOfRange,
                MinBulkDelaySeconds.ToString(CultureInfo.InvariantCulture),
                MaxBulkDelaySeconds.ToString(CultureInfo.InvariantCulture),
                BulkDelaySeconds.ToString(CultureInfo.InvariantCulture)));
        }

        if (BulkMaxPerRun is < MinBulkMaxPerRun or > MaxBulkMaxPerRun)
        {
            problems.Add(new LocalizedText(
                StatusCatalog.SmtpBulkLimitOutOfRange,
                MinBulkMaxPerRun.ToString(CultureInfo.InvariantCulture),
                MaxBulkMaxPerRun.ToString(CultureInfo.InvariantCulture),
                BulkMaxPerRun.ToString(CultureInfo.InvariantCulture)));
        }

        if (string.IsNullOrWhiteSpace(FromAddress))
        {
            problems.Add(new LocalizedText(StatusCatalog.SmtpSenderRequired));
        }
        else if (!LooksLikeAddress(FromAddress))
        {
            problems.Add(new LocalizedText(StatusCatalog.SmtpSenderNotAnAddress, FromAddress));
        }

        if (!string.IsNullOrWhiteSpace(Host))
        {
            if (Port is < 1 or > 65535)
            {
                problems.Add(new LocalizedText(
                    StatusCatalog.SmtpPortOutOfRange,
                    Port.ToString(CultureInfo.InvariantCulture)));
            }

            // ⛔ The one combination that is refused rather than warned about: a password on a connection
            //    that never becomes encrypted is a password on the wire.
            if (Security == SmtpSecurity.None && !string.IsNullOrWhiteSpace(Username))
            {
                problems.Add(new LocalizedText(StatusCatalog.SmtpUsernameNeedsStartTls));
            }

            if (!string.IsNullOrWhiteSpace(Password) && string.IsNullOrWhiteSpace(Username))
            {
                problems.Add(new LocalizedText(StatusCatalog.SmtpPasswordNeedsUsername));
            }
        }
        else if (!string.IsNullOrWhiteSpace(Username) || !string.IsNullOrWhiteSpace(Password))
        {
            problems.Add(new LocalizedText(StatusCatalog.SmtpCredentialsWithoutServer));
        }

        return problems;
    }

    // ⚠ Deliberately shallow. A real address grammar is RFC 5322 and nothing here needs it: the check
    //    exists to catch a typed mistake, and the SERVER is the authority on whether an address works.
    // ⭐ Internal rather than private since L6.2: LicenseMessageComposer asks the same question about the
    //    RECIPIENT, and one shallow check answering it for both ends is the point — a second copy would be
    //    a second opinion about what an address looks like.
    internal static bool LooksLikeAddress(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0 &&
               at < value.Length - 1 &&
               value.IndexOf('@', at + 1) < 0 &&
               !value.Contains(' ', StringComparison.Ordinal);
    }
}
