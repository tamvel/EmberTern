using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;

namespace EmberTern.LicenseManager.Services;

/// <summary>
/// Delivers a composed licence message and records what happened.
///
/// <para>⭐⭐ <b>The register write is HERE, not in a sender.</b> A sender is transport — it may one day be
/// MailKit or an HTTP API if the company mailbox forces it (§48.1) — and history must not be a thing each
/// transport remembers to write. ⛔ Equally, it is not in the view model: an audit line that depended on a
/// window being open would be missing from exactly the runs nobody watched.</para>
///
/// <para>⭐⭐ <b>A FAILED attempt is recorded too.</b> <c>licence.send-failed</c> is not bookkeeping for its
/// own sake: the question the register must answer months later is <i>"did we try to send this, and what
/// happened?"</i>, and a history that only ever shows successes cannot distinguish "we never sent it" from
/// "we tried four times and their server refused". ⚠ The server's own words go into the note, verbatim.</para>
///
/// <para>⛔ <b>Nothing here composes, signs or re-wraps anything.</b> It sends the message it is handed —
/// the same value the operator confirmed in the preview — and the attachment inside it came from the
/// stored artifact (§50.3).</para>
/// </summary>
public sealed class LicenceDelivery
{
    // ⛔ Persisted verbatim into an append-only table, so these strings are part of the register's
    //    vocabulary: never renamed, never reused. The design named both (§14.1).
    private const string ActionSent = "licence.sent";
    private const string ActionFailed = "licence.send-failed";

    // ⭐ The action that already exists for "a file carrying this artifact left the manager"
    //    (IssuingWorkflow.SaveArtifact writes it). An .eml is exactly that, plus a covering message — so
    //    it reuses the vocabulary rather than adding a value that would mean the same thing.
    private const string ActionExported = "licence.exported";

    private const string TargetType = "licence";

    private readonly LicenseRegister _register;

    /// <summary>Creates the delivery over the register that records it.</summary>
    public LicenceDelivery(LicenseRegister register) =>
        _register = register ?? throw new ArgumentNullException(nameof(register));

    /// <summary>
    /// Sends, then records the outcome — success or failure, always exactly one line.
    ///
    /// <para>⚠ The recording happens AFTER the attempt and never before it: a line written first would
    /// claim a send that a crash could prevent, and this register's whole value is that it does not
    /// overstate what reached the customer.</para>
    /// </summary>
    public async Task<SendOutcome> SendAsync(
        ILicenseEmailSender sender, LicenseMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(message);

        var outcome = await sender
            .SendAsync(OutgoingEmail.ForLicence(message), cancellationToken)
            .ConfigureAwait(false);

        _register.Record(
            outcome.Sent ? ActionSent : ActionFailed,
            TargetType,
            message.LicenseId,
            outcome.Sent
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Sent to {message.ToAddress} via {outcome.Delivered} ({message.Language}).")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Sending to {message.ToAddress} via {sender.Destination} failed: {outcome.Error}"));

        return outcome;
    }

    /// <summary>
    /// Writes the message as a file the operator sends themselves, and records it as an EXPORT.
    ///
    /// <para>⭐⭐ ⛔ <b>It is deliberately NOT recorded as <c>licence.sent</c>.</b> Nothing has been sent —
    /// a file exists, and whether it ever reaches the customer is a decision taken later in a mail client
    /// this application cannot see. Claiming otherwise would put a false statement in an append-only
    /// history, which is the one thing a register of record must never do.</para>
    ///
    /// <para>⚠ A FAILED write records nothing, matching <c>SaveArtifact</c>: a file that was not written
    /// is not an event in the licence's life, and the operator is looking at the error.</para>
    /// </summary>
    public async Task<SendOutcome> ExportAsync(
        ILicenseEmailSender sender, LicenseMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(message);

        var outcome = await sender
            .SendAsync(OutgoingEmail.ForLicence(message), cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Sent)
        {
            _register.Record(
                ActionExported,
                TargetType,
                message.LicenseId,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Path.GetFileName(outcome.Delivered)} — a message for {message.ToAddress}, " +
                    $"to be sent by hand."));
        }

        return outcome;
    }
}
