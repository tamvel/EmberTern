using System;

namespace EmberTern.LicenseManager.Email;

/// <summary>
/// One licence e-mail, composed and complete — everything a sender needs and nothing it has to decide.
///
/// <para>⭐⭐ <b>A VALUE, not an operation.</b> It performs no I/O, holds no connection and knows nothing
/// about SMTP: <see cref="LicenseMessageComposer"/> produces it and L6.3's sender consumes it. That split
/// is what makes the whole of "can we build the right message?" answerable without a network, and it is
/// why the attachment travels here as BYTES rather than as a path — a message that referred to a file
/// would be a message that could be composed correctly and sent wrong.</para>
///
/// <para>⭐ <b>The attachment is the STORED artifact, byte for byte.</b> Not a fresh signature, not a
/// re-wrapped token: exactly what <c>IssuingWorkflow.SaveArtifact</c> writes for the same artifact, from
/// the one function that defines those bytes (<c>IssuingWorkflow.ArtifactBytes</c>). ⛔ Composing a
/// message must never issue anything — a new <c>iat</c> is a replacement the client would install over
/// the licence the customer already has (§16.4).</para>
/// </summary>
public sealed record LicenseMessage
{
    /// <summary>
    /// The attachment's media type. ⛔ Always this.
    ///
    /// <para>⚠ Deliberately not a made-up <c>application/x-embertern-licence</c>: no mail client knows
    /// such a type, and an unknown type is treated worse by filters than an opaque one. The file is a
    /// blob the customer saves and drops on a window; <c>octet-stream</c> is exactly that claim.</para>
    /// </summary>
    public const string OctetStream = "application/octet-stream";

    /// <summary>Where it goes — the customer's address, from the register.</summary>
    public required string ToAddress { get; init; }

    /// <summary>
    /// The display name beside it — the licensee, exactly as it was SIGNED.
    ///
    /// <para>⚠ Carries customer data into a header, so it is one of the three headers the Unicode guard
    /// covers: a Polish company name must survive transport encoding unchanged.</para>
    /// </summary>
    public required string ToName { get; init; }

    /// <summary>Who it comes from — the configured sender address.</summary>
    public required string FromAddress { get; init; }

    /// <summary>The sender's display name. ⚠ Falls back to the address when none is configured.</summary>
    public required string FromName { get; init; }

    /// <summary>The subject, from the plain-text template's <c>Subject:</c> line, substituted.</summary>
    public required string Subject { get; init; }

    /// <summary>
    /// The plain-text body.
    ///
    /// <para>⚠ Not optional and not a courtesy (§14.2): some corporate clients strip HTML, and a message
    /// whose licence details existed only in the HTML half would arrive empty for those customers.</para>
    /// </summary>
    public required string TextBody { get; init; }

    /// <summary>The HTML body. ⭐ Values in it are HTML-encoded; the same values are raw in the text body.</summary>
    public required string HtmlBody { get; init; }

    /// <summary>⛔ Always <c>EmberTern.etlic</c> — never the customer's name, never a timestamp (O6).</summary>
    public required string AttachmentFileName { get; init; }

    /// <summary>⛔ Always <see cref="OctetStream"/>.</summary>
    public required string AttachmentMediaType { get; init; }

    /// <summary>
    /// The armored artifact, byte for byte as the exported file.
    ///
    /// <para>⚠ An array, so ⛔ do not mutate it — record equality compares it by reference, which is
    /// correct here (two compositions of the same artifact are two arrays holding the same bytes, and the
    /// thing worth asserting is the CONTENT, which the guards compare explicitly).</para>
    /// </summary>
    public required byte[] AttachmentBytes { get; init; }

    /// <summary>
    /// Which language this message was written in — the resolved code, never the raw setting.
    ///
    /// <para>⭐ Recorded on the value so a caller can say what it is about to send without re-deriving the
    /// fallback rule that <see cref="MessageLanguages.Resolve"/> owns.</para>
    /// </summary>
    public required string Language { get; init; }

    /// <summary>
    /// Which licence this is about — the <c>lid</c> carried by the attached artifact.
    ///
    /// <para>⭐⭐ <b>Added in L6.3 because a real consumer appeared, not in case one would</b>: the audit
    /// line for a send needs a target, and taking it from the SIGNED payload rather than from whatever the
    /// caller happens to be holding is what keeps <c>licence.sent</c> pointing at the licence that was
    /// actually delivered. ⛔ L6.2 deliberately left it off while nothing needed it.</para>
    /// </summary>
    public required string LicenseId { get; init; }
}
