using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EmberTern.Licensing;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;

namespace EmberTern.LicenseManager.Email;

/// <summary>
/// Turns a STORED artifact, a customer and the SMTP settings into a finished <see cref="LicenseMessage"/>.
///
/// <para>⭐⭐ <b>Pure, and that is the whole design.</b> No file is read or written, no register is touched,
/// nothing is signed, no socket is opened and no Avalonia type is named. Composition is therefore fully
/// testable without a mail server — which matters because the company mailbox is still unmeasured (§48.1)
/// and the question <i>"can we build the right message?"</i> must not wait on it.</para>
///
/// <para>⭐⭐ <b>THE ATTACHMENT COMES FROM THE REGISTER, NEVER FROM A NEW SIGNATURE.</b> The caller hands in
/// the artifact <c>license_current_artifact</c> points at — the same authority <c>InspectLatest</c> uses —
/// and this class turns it into bytes through <see cref="IssuingWorkflow.ArtifactBytes"/>, the one function
/// that defines what <c>EmberTern.etlic</c> contains. ⛔ Issuing here would mint a new <c>iat</c>, which the
/// client installs as a REPLACEMENT for the licence the customer already holds (§16.4) — an e-mail must
/// never have that effect.</para>
///
/// <para>⭐ <b>The words and the attachment come from the same bytes.</b> Every fact in the body is read
/// out of the payload carried by the token being attached, so the message and the licence cannot disagree
/// — §14.2's rule, implemented rather than promised. ⛔ Nothing is read from the licence ROW: saved terms
/// that have not been issued yet are a legitimate state, and a message built from them would describe a
/// licence nobody holds.</para>
/// </summary>
public static class LicenseMessageComposer
{
    // ⚠ Strict on purpose. A loose pattern would silently accept `{{ Seats }}` and leave it in the sent
    //    message; anything shaped like a placeholder that this does not match is caught by the sweep at
    //    the end of Fill, so a template can never ship a substitution nobody performs.
    private static readonly Regex Placeholder = new(@"\{\{([A-Za-z]+)\}\}", RegexOptions.CultureInvariant);

    /// <summary>
    /// Everything that stops a message being composed, in the order an operator would fix it. Empty means
    /// it can be composed.
    ///
    /// <para>⭐⭐ <b>About COMPOSITION alone — deliberately narrower than <see cref="SmtpSettings.Validate"/>.</b>
    /// A message needs a recipient, a sender and a readable artifact; a host, a port and credentials are
    /// the SENDER's requirements, and an <c>.eml</c> handed to Outlook needs none of them. ⛔ Folding the
    /// settings' whole verdict in here would also refuse over the one thing that must never fail a
    /// message: an unrecognised message language, which <see cref="MessageLanguages.Resolve"/> answers with
    /// the default on purpose.</para>
    ///
    /// <para>⚠ Whether the server accepts the message, and whether the customer's address exists, are
    /// questions only a server can answer.</para>
    /// </summary>
    public static IReadOnlyList<string> Problems(
        IssuedArtifactRecord artifact, CustomerRecord customer, SmtpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(settings);

        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(customer.Email))
        {
            problems.Add(
                $"{customer.Name} has no e-mail address. Add one on the Customer page, or export the " +
                "licence to a file and deliver it another way.");
        }
        else if (!SmtpSettings.LooksLikeAddress(customer.Email))
        {
            problems.Add($"The customer's e-mail address does not look like one: {customer.Email}");
        }

        // ⚠ The SENDER address only — asked through the same shallow check the settings use, so the two
        //    ends of the message are judged by one opinion about what an address looks like.
        if (string.IsNullOrWhiteSpace(settings.FromAddress) ||
            !SmtpSettings.LooksLikeAddress(settings.FromAddress))
        {
            problems.Add(
                "There is no usable sender address in the e-mail settings, and a message has to come from " +
                "somewhere. Set one under Settings ▸ E-mail.");
        }

        // ⚠ Last, because it is the one fault that is OURS rather than the operator's: an artifact whose
        //    stored token cannot be read is a register problem, and no field on any window would fix it.
        if (ReadPayload(artifact) is null)
        {
            problems.Add(
                $"The stored artifact for licence {artifact.LicenseId} could not be read, so no message " +
                "can describe it. Inspect it in the issuing history before sending anything.");
        }

        return problems;
    }

    /// <summary>
    /// Composes the message.
    /// </summary>
    /// <param name="artifact">
    /// ⭐ The artifact the register marks CURRENT for the licence — <c>LicenseRegister.GetCurrentArtifact</c>.
    /// ⛔ Never <c>Artifacts[0]</c>, and never a freshly signed one.
    /// </param>
    /// <param name="customer">Whose licence it is. ⚠ Only the ADDRESS is taken from here — see the type remarks.</param>
    /// <param name="settings">The sender, and the language the message is written in.</param>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Problems"/> is not empty. ⭐ Refused rather than composed half-way: a message with no
    /// recipient, or one describing an artifact we cannot read, is not something to hand to a sender and
    /// let it fail later.
    /// </exception>
    public static LicenseMessage Compose(
        IssuedArtifactRecord artifact, CustomerRecord customer, SmtpSettings settings)
    {
        var problems = Problems(artifact, customer, settings);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "This licence cannot be sent yet: " + string.Join(" ", problems));
        }

        var payload = ReadPayload(artifact)!;
        var language = MessageLanguages.Resolve(settings.MessageLanguage);

        // ⭐ The MESSAGE's culture, never the operator's. The customer reads the dates, and the language
        //    they are written in is a setting the operator chose for them (D-9).
        var culture = CultureInfo.GetCultureInfo(language);
        var values = MessagePlaceholders.Values(payload, settings, culture);

        return new LicenseMessage
        {
            ToAddress = customer.Email!.Trim(),

            // ⭐ The SIGNED name, not the register's current one: the message must address the customer the
            //    way their licence does, or a corrected company name would make the two disagree.
            ToName = payload.Licensee,
            FromAddress = settings.FromAddress.Trim(),
            FromName = values[MessagePlaceholders.SenderName],

            Subject = Fill(LicenseEmailTemplates.LoadSubject(language), values, encodeHtml: false),
            TextBody = Fill(
                LicenseEmailTemplates.LoadBody(language, MessageBodyKind.Text), values, encodeHtml: false),
            HtmlBody = Fill(
                LicenseEmailTemplates.LoadBody(language, MessageBodyKind.Html), values, encodeHtml: true),

            AttachmentFileName = LicenseConstants.DeliveredFileName,
            AttachmentMediaType = LicenseMessage.OctetStream,
            AttachmentBytes = IssuingWorkflow.ArtifactBytes(artifact),

            Language = language,

            // ⭐ From the payload, like everything else the message asserts — so the audit line a send
            //    writes names the licence that is actually in the attachment.
            LicenseId = payload.LicenseId,
        };
    }

    /// <summary>
    /// The payload of the token that is about to be attached, or <see langword="null"/> if it cannot be
    /// read.
    ///
    /// <para>⭐⭐ Read out of <see cref="IssuedArtifactRecord.Token"/> rather than out of the record's
    /// <c>PayloadJson</c> column. They hold the same bytes today, and reading the token is what keeps that
    /// true by construction: the message then describes the very artifact it carries, with no second
    /// source that could drift.</para>
    ///
    /// <para>⚠ Nothing here authenticates anything. The message is composed from a token we issued and
    /// stored ourselves; the SIGNATURE is what the customer's copy of EmberTern checks, and a guard proves
    /// the attached bytes still verify after transport.</para>
    /// </summary>
    private static LicensePayload? ReadPayload(IssuedArtifactRecord artifact) =>
        LicenseEnvelope.TryParse(artifact.Token, out var envelope, out _) &&
        LicensePayload.TryParse(envelope.PayloadJson, out var payload, out _)
            ? payload
            : null;

    /// <summary>
    /// Substitutes every placeholder, encoding the VALUES for HTML where the body is HTML.
    ///
    /// <para>⭐⭐ The encoding is applied to the substituted value and never to the template, which is the
    /// only arrangement that can be right: a company name containing <c>&amp;</c> must not break the
    /// markup, and the markup itself must not be escaped into visible tags.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The template asks for a placeholder that is not in <see cref="MessagePlaceholders.All"/>, or leaves
    /// something shaped like one behind. ⭐ A build fault, and loud: the alternative is a customer reading
    /// <c>{{Seats}}</c> in their licence e-mail.
    /// </exception>
    private static string Fill(
        string template, IReadOnlyDictionary<string, string> values, bool encodeHtml)
    {
        var filled = Placeholder.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (!values.TryGetValue(name, out var value))
            {
                throw new InvalidOperationException(
                    $"The message template asks for '{{{{{name}}}}}', which is not a placeholder this " +
                    "build knows. Add it to MessagePlaceholders, or correct the template.");
            }

            return encodeHtml ? EncodeForHtml(value) : value;
        });

        // ⚠ The sweep, not a formality: the regex above only matches the exact form, so a mistyped
        //    `{{ Seats }}` or `{{seats}` would otherwise travel to the customer verbatim.
        var stray = filled.IndexOf("{{", StringComparison.Ordinal);
        if (stray >= 0)
        {
            throw new InvalidOperationException(
                "A message template left an unfilled placeholder: " +
                filled.Substring(stray, Math.Min(24, filled.Length - stray)));
        }

        return filled;
    }

    /// <summary>
    /// Makes a value safe to place in HTML text or in a double-quoted attribute.
    ///
    /// <para>⚠⚠ <b>Deliberately NOT <see cref="WebUtility.HtmlEncode"/>, and this is the reason:</b> that
    /// method also encodes every non-ASCII character as a numeric entity, so <c>Żółć Sp. z o.o.</c> becomes
    /// <c>&amp;#379;&amp;#243;…</c>. It renders correctly, and it is still wrong here — the body is sent as
    /// UTF-8, the entity form makes the message unreadable in its own source, and a customer name would no
    /// longer be findable in the sent item. ⭐ Only the four characters that can change the MARKUP are
    /// encoded; every letter travels as itself.</para>
    /// </summary>
    private static string EncodeForHtml(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
