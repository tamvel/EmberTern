using System;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.LicenseManager.Email;

/// <summary>
/// Writes a ready <c>.eml</c> the operator opens in their own mail client.
///
/// <para>⭐⭐ <b>Not a rule-satisfying second implementation — the fallback the design named from the
/// start</b> (§14.3). It is the answer when a mailbox refuses basic auth, when a relay strips
/// attachments, when the send fails at 17:55 on a Friday, and for the operator who simply prefers to send
/// from Outlook so the message lands in their own Sent items. ⭐ It works regardless of how the company
/// SMTP question turns out, which is exactly why it exists before that question is answered.</para>
///
/// <para>⚠ <b>The file is the SAME message</b> — same builder, same encodings, same attachment bytes
/// (<see cref="MailComposition"/>). ⛔ Not a preview or an export format: it is the message, saved.</para>
///
/// <para>⚠ It writes to the path the operator chose, which the BCL cannot do directly:
/// <see cref="SmtpDeliveryMethod.SpecifiedPickupDirectory"/> names a FOLDER and invents a GUID file name.
/// So the message is written into a temporary folder of its own and moved. ⛔ Not worth hand-writing MIME
/// to avoid — that would be a second construction of the very thing this class exists to keep identical.
/// </para>
/// </summary>
public sealed class EmlFileEmailSender : ILicenseEmailSender
{
    /// <summary>The extension every mail client recognises.</summary>
    public const string FileExtension = ".eml";

    private readonly string _path;

    /// <summary>Creates the sender for one destination file.</summary>
    /// <param name="path">Where to write. ⚠ Overwritten if it exists — the operator chose it in a Save-As.</param>
    public EmlFileEmailSender(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <inheritdoc />
    public string Destination => _path;

    /// <inheritdoc />
    public Task<SendOutcome> SendAsync(
        OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        cancellationToken.ThrowIfCancellationRequested();

        var staging = Path.Combine(
            Path.GetTempPath(), "embertern-lm-eml", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(staging);

            using (var mail = MailComposition.Build(email))
            using (var client = new SmtpClient
                   {
                       DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                       PickupDirectoryLocation = staging,
                   })
            {
                // ⭐ No socket is opened on this path, and no server is contacted — Send() here is a file
                //    write. That is what makes the .eml route work with no configuration beyond a sender
                //    address.
                client.Send(mail);
            }

            var written = Directory.GetFiles(staging).Single();

            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.Move(written, _path, overwrite: true);

            return Task.FromResult(SendOutcome.Ok(_path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SmtpException
                                      or InvalidOperationException)
        {
            return Task.FromResult(SendOutcome.Failed(e.Message));
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch (IOException)
            {
                // ⚠ A leftover temporary folder is not worth failing a delivery over.
            }
        }
    }
}
