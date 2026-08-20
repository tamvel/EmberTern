using System;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.LicenseManager.Email;

/// <summary>
/// How an attempt to deliver ended.
///
/// <para>⭐⭐ <b>A VALUE, not an exception.</b> A refused login, an unreachable host and a rejected
/// recipient are ordinary outcomes of asking an outside server for something — they are what the operator
/// opened the window to find out, and they must be RECORDED (<c>licence.send-failed</c>) rather than
/// thrown past the caller. ⛔ A sender that threw would make the audit line optional in practice.</para>
/// </summary>
public sealed record SendOutcome
{
    /// <summary>Whether the message left this application.</summary>
    public required bool Sent { get; init; }

    /// <summary>
    /// Where it went — a host, or the path of the file that was written. ⚠ Shown to the operator and
    /// written into the audit note, so it must be something they can act on.
    /// </summary>
    public string Delivered { get; init; } = string.Empty;

    /// <summary>
    /// What went wrong, in the server's own words.
    ///
    /// <para>⭐ Verbatim, deliberately — the same rule EmberTern applies to connection errors: the server
    /// is the authority, and an interpretation layer misfires (a wrong password and a blocked app
    /// password produce different <c>535</c> texts, and only the text distinguishes them).</para>
    /// </summary>
    public string? Error { get; init; }

    /// <summary>It left.</summary>
    public static SendOutcome Ok(string delivered) => new() { Sent = true, Delivered = delivered };

    /// <summary>It did not.</summary>
    public static SendOutcome Failed(string error) => new() { Sent = false, Error = error };
}

/// <summary>
/// Delivers what this application sends.
///
/// <para>⭐ <b>Two real implementations, not an interface for its own sake</b> (Architecture rule 2):
/// <see cref="SmtpLicenseEmailSender"/> talks to a server, and <see cref="EmlFileEmailSender"/> writes a
/// ready <c>.eml</c> the operator opens in their own mail client. The second is not a stub — it is the
/// answer when a corporate mailbox refuses basic auth, when a relay strips attachments, or when the
/// operator simply prefers to send from Outlook, and it works whatever the SMTP question turns out to
/// be (§14.3).</para>
///
/// <para>⛔ <b>A sender does not touch the register.</b> Recording <c>licence.sent</c> /
/// <c>licence.send-failed</c> is <see cref="Services.LicenceDelivery"/>'s job — a transport that also
/// wrote history would make the two impossible to test apart, and would put an audit decision inside the
/// class most likely to be replaced (MailKit, an HTTP API) if the company mailbox forces it.</para>
///
/// <para>⛔ It also never composes anything: what it is handed is what it sends.</para>
/// </summary>
public interface ILicenseEmailSender
{
    /// <summary>
    /// Where this sender delivers, in words — a host, or a file path. ⭐ For the message the operator
    /// reads afterwards, so "sent" never has to mean "somewhere".
    /// </summary>
    string Destination { get; }

    /// <summary>
    /// Delivers, and reports how it went. ⛔ Never throws for a transport failure — see
    /// <see cref="SendOutcome"/>.
    /// </summary>
    Task<SendOutcome> SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default);
}
