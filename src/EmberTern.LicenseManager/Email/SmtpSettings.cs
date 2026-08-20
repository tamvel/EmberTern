using System;
using System.Collections.Generic;
using System.Globalization;

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
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        // ⚠ Checked, but never REPAIRED here: an unrecognised code resolves to the default at the moment a
        //   message is composed (MessageLanguages.Resolve). A settings file is allowed to carry a language
        //   this build does not know — refusing to save because of it would fail the whole configuration
        //   over a preference that has a safe answer.
        if (!MessageLanguages.IsSupported(MessageLanguage))
        {
            problems.Add($"The message language '{MessageLanguage}' is not one this version can write.");
        }

        if (string.IsNullOrWhiteSpace(FromAddress))
        {
            problems.Add("A sender address is required — it is what the customer replies to.");
        }
        else if (!LooksLikeAddress(FromAddress))
        {
            problems.Add($"The sender address does not look like an e-mail address: {FromAddress}");
        }

        if (!string.IsNullOrWhiteSpace(Host))
        {
            if (Port is < 1 or > 65535)
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture, $"The port must be between 1 and 65535, not {Port}."));
            }

            // ⛔ The one combination that is refused rather than warned about: a password on a connection
            //    that never becomes encrypted is a password on the wire.
            if (Security == SmtpSecurity.None && !string.IsNullOrWhiteSpace(Username))
            {
                problems.Add(
                    "A username cannot be used without STARTTLS — the password would travel unencrypted. " +
                    "Either enable STARTTLS or clear the username.");
            }

            if (!string.IsNullOrWhiteSpace(Password) && string.IsNullOrWhiteSpace(Username))
            {
                problems.Add("A password without a username cannot be used. Enter the account that signs in.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(Username) || !string.IsNullOrWhiteSpace(Password))
        {
            problems.Add(
                "Credentials were entered but no server. Enter the SMTP host, or clear the credentials " +
                "and deliver the message as a file instead.");
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
