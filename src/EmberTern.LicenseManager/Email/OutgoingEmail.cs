using System;

namespace EmberTern.LicenseManager.Email;

/// <summary>
/// A file riding on a message.
/// </summary>
/// <param name="FileName">What the customer sees when they save it.</param>
/// <param name="MediaType">⛔ <see cref="LicenseMessage.OctetStream"/> for a licence.</param>
/// <param name="Content">The bytes, exactly as they must arrive.</param>
public sealed record EmailAttachment(string FileName, string MediaType, byte[] Content);

/// <summary>
/// What a sender puts on the wire — the transport's own shape.
///
/// <para>⭐⭐ <b>Why this exists, given that <see cref="LicenseMessage"/> already describes a message:</b>
/// the License Manager sends exactly two kinds of thing, a licence and the one SMTP configuration test,
/// and the test carries no attachment. Making <see cref="LicenseMessage.AttachmentBytes"/> optional to fit
/// the test would have weakened the guarantee that matters most — a licence e-mail ALWAYS carries its
/// artifact. ⭐ So the two compose to one transport value instead, and there is exactly ONE code path from
/// here to the server: the test therefore proves the configuration the real send will use, which is the
/// only reason to have a test at all.</para>
///
/// <para>⛔ <b>This is not a "generic e-mail system".</b> No queue, no address book, no templating, no
/// scheduling, no retries — six fields and an optional file. If a third kind of message ever appears, it
/// composes to this; nothing here grows.</para>
/// </summary>
public sealed record OutgoingEmail
{
    /// <summary>Where it goes.</summary>
    public required string ToAddress { get; init; }

    /// <summary>The display name beside it, if any.</summary>
    public string ToName { get; init; } = string.Empty;

    /// <summary>Who it comes from.</summary>
    public required string FromAddress { get; init; }

    /// <summary>The sender's display name.</summary>
    public string FromName { get; init; } = string.Empty;

    /// <summary>The subject.</summary>
    public required string Subject { get; init; }

    /// <summary>The plain-text body. ⚠ Never empty — some clients show only this one.</summary>
    public required string TextBody { get; init; }

    /// <summary>The HTML alternative. ⚠ Optional: a message may legitimately be plain text only.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>The attachment, when there is one.</summary>
    public EmailAttachment? Attachment { get; init; }

    /// <summary>
    /// The transport form of a composed licence message.
    ///
    /// <para>⭐⭐ <b>A projection, never a recomposition.</b> It carries the SAME subject, bodies and bytes
    /// the operator was shown in the preview — that is what makes "what you confirmed is what was sent"
    /// a property of the code rather than a promise in a comment.</para>
    /// </summary>
    public static OutgoingEmail ForLicence(LicenseMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new OutgoingEmail
        {
            ToAddress = message.ToAddress,
            ToName = message.ToName,
            FromAddress = message.FromAddress,
            FromName = message.FromName,
            Subject = message.Subject,
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
            Attachment = new EmailAttachment(
                message.AttachmentFileName, message.AttachmentMediaType, message.AttachmentBytes),
        };
    }
}
