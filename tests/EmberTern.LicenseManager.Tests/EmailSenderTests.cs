using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EmberTern.Licensing;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The two senders.
///
/// <para>⭐⭐ <b>The <c>.eml</c> sender is exercised for real</b> — it writes a file, and the guards read
/// that file back: the licence inside it still verifies through <c>EmberTern.Licensing</c>, and Polish
/// characters survive every header. ⛔ Nothing about it is faked, because nothing about it needs a network.
/// </para>
///
/// <para>⚠ <b>The SMTP sender cannot be proved here and is not pretended to be.</b> A test that reached a
/// real server would be a test of that server's availability; what IS asserted is everything decidable
/// without one — that it refuses to exist without a host, and that its transport choices are the ones
/// §48.4 fixed. The real proof is the operator's own end-to-end send, which is what L6.3 exists to
/// enable.</para>
/// </summary>
public sealed class EmailSenderTests : IDisposable
{
    private readonly ManagerFixture _manager = new();

    private static SmtpSettings Settings => new()
    {
        Host = "smtp.example.test",
        FromAddress = "licencje@example.test",
        FromName = "EmberTern — licencje",
        MessageLanguage = MessageLanguages.Polish,
    };

    private LicenseMessage Compose(string name = "Żółć Sp. z o.o.")
    {
        var customer = _manager.SaveCustomer(name, "biuro@zolc.test");
        var licence = _manager.SaveLicense(customer);
        _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        return LicenseMessageComposer.Compose(
            _manager.Register.GetCurrentArtifact(licence.LicenseId)!, customer, Settings);
    }

    // ── The file sender ─────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ It writes where it was told, not where the BCL would have put it.</summary>
    [Fact]
    public async Task TheFileSenderWritesTheChosenPath()
    {
        var path = Path.Combine(_manager.Paths.Root, "chosen name.eml");

        var outcome = await new EmlFileEmailSender(path).SendAsync(
            OutgoingEmail.ForLicence(Compose()));

        Assert.True(outcome.Sent, outcome.Error);
        Assert.Equal(path, outcome.Delivered);
        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// ⭐⭐ <b>The whole delivery, end to end, without a network:</b> compose → write the message → read the
    /// attachment out of the file → verify it with the code the customer runs.
    /// </summary>
    [Fact]
    public async Task TheLicenceInTheWrittenFileStillVerifies()
    {
        var message = Compose();
        var path = Path.Combine(_manager.Paths.Root, "licence.eml");

        await new EmlFileEmailSender(path).SendAsync(OutgoingEmail.ForLicence(message));

        var delivered = MimeProbe.Attachment(path, LicenseConstants.DeliveredFileName);
        Assert.Equal(message.AttachmentBytes, delivered);

        var verdict = LicenseVerifier.Verify(
            Encoding.UTF8.GetString(delivered),
            new LicenseVerificationContext(
                _manager.Session.TrustedKeys,
                _manager.Now,
                LicenseConstants.ProductId,
                LicenseConstants.MaxSupportedPayloadVersion,
                LicenseConstants.DefaultGracePeriod,
                BuildReleaseDate: null));

        Assert.Equal(LicenseStatus.Valid, verdict.Status);
        Assert.Equal("Żółć Sp. z o.o.", verdict.Payload!.Licensee);
    }

    /// <summary>⚠ And the headers a customer's client shows survive the same trip.</summary>
    [Fact]
    public async Task PolishCharactersSurviveTheWrittenMessage()
    {
        var message = Compose();
        var path = Path.Combine(_manager.Paths.Root, "polish.eml");

        await new EmlFileEmailSender(path).SendAsync(OutgoingEmail.ForLicence(message));

        Assert.Equal(message.Subject, MimeProbe.Header(path, "Subject"));
        Assert.Contains("Żółć Sp. z o.o.", MimeProbe.Header(path, "To"), StringComparison.Ordinal);
        Assert.Equal(message.TextBody, MimeProbe.TextBody(path));
        Assert.Equal(message.HtmlBody, MimeProbe.HtmlBody(path));
    }

    /// <summary>⚠ A failure is reported, not thrown — the contract every sender shares.</summary>
    [Fact]
    public async Task TheFileSenderReportsAFailureRatherThanThrowing()
    {
        var asFolder = Path.Combine(_manager.Paths.Root, "occupied");
        Directory.CreateDirectory(asFolder);

        var outcome = await new EmlFileEmailSender(asFolder).SendAsync(
            OutgoingEmail.ForLicence(Compose()));

        Assert.False(outcome.Sent);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Error));
    }

    // ── The SMTP sender ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Settings with no host cannot build a sender at all. ⛔ Refused at construction rather than at
    /// send time, where it would read as a network problem.
    /// </summary>
    [Fact]
    public void TheSmtpSenderRefusesSettingsWithNoServer()
    {
        var noHost = Settings with { Host = string.Empty };

        Assert.False(noHost.CanSendDirectly);
        Assert.Throws<ArgumentException>(() => new SmtpLicenseEmailSender(noHost));
    }

    /// <summary>⭐ It names where it delivers, so "sent" never has to mean "somewhere".</summary>
    [Fact]
    public void TheSmtpSenderNamesItsDestination() =>
        Assert.Equal("smtp.example.test", new SmtpLicenseEmailSender(Settings).Destination);

    /// <summary>
    /// ⚠ A bounded timeout, deliberately: <c>SmtpClient</c>'s own default is 100 seconds, long enough that
    /// a mistyped host reads as a frozen application while the operator watches a modal window.
    /// </summary>
    [Fact]
    public void TheSmtpSenderDoesNotWaitForeverForAWrongHost() =>
        Assert.InRange(SmtpLicenseEmailSender.TimeoutMilliseconds, 1_000, 60_000);

    public void Dispose() => _manager.Dispose();
}
