using System;
using System.IO;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace EmberTern.LicenseManager.Email;

/// <summary>
/// The ONE place an <see cref="OutgoingEmail"/> becomes a <see cref="MailMessage"/>.
///
/// <para>⭐⭐ Both senders build the message here, so the <c>.eml</c> an operator opens in Outlook and the
/// message a server receives are the same message — different transports, one construction. ⛔ A second
/// builder is how the two would come to differ in exactly the way nobody checks: encoding.</para>
///
/// <para>⭐ <b>UTF-8 everywhere, and base64 for both bodies.</b> Subject, display names and bodies all
/// carry Polish company names; base64 is the safe transfer encoding for UTF-8 content and it is what makes
/// "the body arrived intact" testable by decoding one thing rather than three. ⚠ Quoted-printable would
/// also be correct — this is a choice, stated so the test that decodes it can rely on it.</para>
///
/// <para>⛔ No tracking pixel, no read receipt, no <c>X-</c> headers of our own (§14.2).</para>
/// </summary>
internal static class MailComposition
{
    /// <summary>Builds the message. ⚠ The caller disposes it.</summary>
    internal static MailMessage Build(OutgoingEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        var mail = new MailMessage
        {
            From = new MailAddress(email.FromAddress, email.FromName, Encoding.UTF8),
            Subject = email.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = email.TextBody,
            BodyEncoding = Encoding.UTF8,
            BodyTransferEncoding = TransferEncoding.Base64,
            IsBodyHtml = false,
        };

        mail.To.Add(new MailAddress(email.ToAddress, email.ToName, Encoding.UTF8));

        // ⚠ The plain-text body stays the message's own Body and the HTML rides as an ALTERNATIVE, in
        //   that order — a client that strips HTML then shows the text, which is the whole reason §14.2
        //   requires both.
        if (!string.IsNullOrEmpty(email.HtmlBody))
        {
            var html = AlternateView.CreateAlternateViewFromString(
                email.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html);
            html.TransferEncoding = TransferEncoding.Base64;
            mail.AlternateViews.Add(html);
        }

        if (email.Attachment is { } file)
        {
            // ⚠ The stream is owned by the Attachment, which the MailMessage disposes with itself — so it
            //   is deliberately NOT wrapped in a `using` here. Disposing it before Send would send an
            //   empty attachment.
            var content = new MemoryStream(file.Content, writable: false);
            mail.Attachments.Add(new Attachment(content, file.FileName, file.MediaType));
        }

        return mail;
    }
}
