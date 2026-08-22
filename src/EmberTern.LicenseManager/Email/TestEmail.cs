using System;
using System.Globalization;

namespace EmberTern.LicenseManager.Email;

/// <summary>
/// The one message this application sends that is not a licence: a check that the SMTP settings work.
///
/// <para>⭐⭐ <b>It exists because the alternative is finding out during a real delivery.</b> The settings
/// window can validate fields; only a server can answer whether the host, the port, STARTTLS and the
/// credentials actually combine into a working submission. ⚠ Until this existed, the first proof a
/// configuration worked was a customer's licence either arriving or not.</para>
///
/// <para>⭐ <b>It goes down the SAME path as a licence</b> — <see cref="OutgoingEmail"/>,
/// <see cref="MailComposition"/>, <see cref="SmtpLicenseEmailSender"/>. A test that used a different code
/// path would prove something about that path instead.</para>
///
/// <para>⛔ <b>No attachment, and no licence anywhere near it.</b> The recipient is an address the operator
/// typed — their own mailbox — and it must never default to a customer's: a diagnostic that can be aimed
/// at a customer by a mis-click is a diagnostic that will be.</para>
///
/// <para>⚠ <b>English, and plain text only — both deliberate.</b> The reader is the OPERATOR, whose
/// interface is this application's, not the customer whose language <c>MessageLanguage</c> describes; and
/// a diagnostic needs no HTML alternative, which is exactly why <see cref="OutgoingEmail.HtmlBody"/> is
/// optional. ⛔ It is not a template resource: templates exist so a CUSTOMER can be written to in their
/// own language, and adding two files per language for a self-test would be paying that price twice for
/// nothing.</para>
///
/// <para>⛔ <b>Nothing about this is recorded in the audit log.</b> <c>audit_log</c> answers questions
/// about licences and customers; a configuration test concerns neither, and a <c>licence.sent</c> line
/// with no licence would be a false entry in an append-only history.</para>
/// </summary>
public static class TestEmail
{
    /// <summary>
    /// What the subject says.
    ///
    /// <para>⭐ It names the application and the reason in the subject line itself, because the person
    /// opening it may be an administrator who did not send it — an unexplained message from a licensing
    /// system reads as a phishing attempt. ⚠ A PROPERTY, never a <c>const</c>: the settings-catalog lesson
    /// from L6.1a (a compiler-inlined literal is a string nothing can ever resolve again).</para>
    /// </summary>
    public static string Subject => "EmberTern License Manager — SMTP configuration test";

    /// <summary>
    /// Composes the test message.
    /// </summary>
    /// <param name="settings">The configuration being tested — its sender address is used as the From.</param>
    /// <param name="recipient">Where to send it. ⛔ Typed by the operator; never a customer's address.</param>
    public static OutgoingEmail Compose(SmtpSettings settings, string recipient)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);

        return new OutgoingEmail
        {
            ToAddress = recipient.Trim(),
            FromAddress = settings.FromAddress.Trim(),
            FromName = string.IsNullOrWhiteSpace(settings.FromName)
                ? settings.FromAddress.Trim()
                : settings.FromName.Trim(),
            Subject = Subject,
            TextBody = Body(settings, recipient.Trim()),

            // ⛔ No HTML alternative and ⛔ no attachment — see the type remarks.
        };
    }

    // ⚠ It states the configuration it PROVED, not the configuration that was typed — the operator is
    //   about to trust this answer for real deliveries, and "it worked" is only useful if they can see
    //   which server and which sender it worked for. ⛔ The username is named; the password never is.
    private static string Body(SmtpSettings settings, string recipient) => string.Create(
        CultureInfo.InvariantCulture,
        $"""
         This is a test message from EmberTern License Manager.

         It was sent to check that the SMTP configuration works. If you are reading it, the settings
         below can deliver mail, and licence e-mails sent from this computer should arrive the same way.

         Server:     {settings.Host}:{settings.Port}
         Security:   {(settings.Security == SmtpSecurity.StartTls ? "STARTTLS" : "none")}
         Sign-in as: {SignIn(settings)}
         From:       {settings.FromAddress}
         Sent to:    {recipient}

         No licence is attached, and nothing was recorded against any customer — this message is only a
         configuration check.
         """);

    private static string SignIn(SmtpSettings settings) =>
        string.IsNullOrWhiteSpace(settings.Username)
            ? "(no authentication)"
            : settings.Username;
}
