using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmberTern.Licensing;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Services;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>What the register learns when a licence is sent — and when it is not.</b>
///
/// <para>The audit half of L6.3. A history that only ever shows successes cannot tell "we never sent it"
/// from "we tried four times and their server refused", which is the question support actually asks
/// months later.</para>
/// </summary>
public sealed class LicenceDeliveryTests : IDisposable
{
    private readonly ManagerFixture _manager = new();
    private readonly LicenceDelivery _delivery;

    public LicenceDeliveryTests() => _delivery = new LicenceDelivery(_manager.Register);

    private static SmtpSettings Settings => new()
    {
        Host = "smtp.example.test",
        FromAddress = "licencje@example.test",
        FromName = "EmberTern — licencje",
        MessageLanguage = MessageLanguages.Polish,
    };

    private (LicenseMessage Message, IssuedArtifactRecord Artifact, LicenseRecord Licence) Compose(
        string name = "ACME Sp. z o.o.")
    {
        var customer = _manager.SaveCustomer(name, "biuro@acme.test");
        var licence = _manager.SaveLicense(customer);
        var issued = _manager.Workflow.Issue(
            _manager.Session, licence, customer, IssueReasons.Initial);

        var current = _manager.Register.GetCurrentArtifact(licence.LicenseId)!;
        return (LicenseMessageComposer.Compose(current, customer, Settings), issued.Artifact, licence);
    }

    private IReadOnlyList<AuditEntry> AuditFor(string licenseId) =>
        _manager.Register.GetAudit(new AuditQuery { TargetType = "licence", TargetId = licenseId });

    // ── Success ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASuccessfulSendIsRecordedAsLicenceSent()
    {
        var (message, _, licence) = Compose();
        var sender = FakeEmailSender.Succeeding("smtp.example.test");

        var outcome = await _delivery.SendAsync(sender, message);

        Assert.True(outcome.Sent);

        var line = Assert.Single(AuditFor(licence.LicenseId), a => a.Action == "licence.sent");
        Assert.Equal("licence", line.TargetType);
        Assert.Equal(licence.LicenseId, line.TargetId);
        Assert.Contains("biuro@acme.test", line.Note!, StringComparison.Ordinal);
        Assert.Contains("smtp.example.test", line.Note!, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ <b>The attachment on the wire is the STORED artifact, byte for byte.</b> The same assertion
    /// L6.2 makes about composition, made again one layer out — because the thing that reaches the
    /// customer is what the SENDER was handed, not what a composer returned.
    /// </summary>
    [Fact]
    public async Task WhatIsSentCarriesTheStoredArtifactUnchanged()
    {
        var (message, artifact, _) = Compose();
        var sender = FakeEmailSender.Succeeding();

        await _delivery.SendAsync(sender, message);

        var wire = Assert.Single(sender.Sent);
        var file = Assert.IsType<EmailAttachment>(wire.Attachment);

        var path = Path.Combine(_manager.Paths.Root, LicenseConstants.DeliveredFileName);
        _manager.Workflow.SaveArtifact(artifact, path);

        Assert.Equal(File.ReadAllBytes(path), file.Content);
        Assert.Equal("EmberTern.etlic", file.FileName);
        Assert.Equal("application/octet-stream", file.MediaType);

        // ⭐ And it still verifies — the licence a customer would install.
        var verdict = LicenseVerifier.Verify(
            Encoding.UTF8.GetString(file.Content),
            new LicenseVerificationContext(
                _manager.Session.TrustedKeys,
                _manager.Now,
                LicenseConstants.ProductId,
                LicenseConstants.MaxSupportedPayloadVersion,
                LicenseConstants.DefaultGracePeriod,
                BuildReleaseDate: null));

        Assert.Equal(LicenseStatus.Valid, verdict.Status);
    }

    /// <summary>⛔ Sending signs nothing: no new artifact, and the current pointer does not move (§16.4).</summary>
    [Fact]
    public async Task SendingSignsNothingAndMovesNoPointer()
    {
        var (message, _, licence) = Compose();

        var artifactsBefore = _manager.Register.GetArtifacts(licence.LicenseId).Count;
        var currentBefore = _manager.Register.GetCurrentArtifact(licence.LicenseId)!.ArtifactId;

        await _delivery.SendAsync(FakeEmailSender.Succeeding(), message);

        Assert.Equal(artifactsBefore, _manager.Register.GetArtifacts(licence.LicenseId).Count);
        Assert.Equal(currentBefore, _manager.Register.GetCurrentArtifact(licence.LicenseId)!.ArtifactId);
    }

    // ── Failure ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Decision P‑2: a failed attempt is recorded too</b>, with the server's own words, so the
    /// register can answer "did we try, and what happened?".
    /// </summary>
    [Fact]
    public async Task AFailedSendIsRecordedWithTheServersOwnWords()
    {
        var (message, _, licence) = Compose();
        var sender = FakeEmailSender.Failing("5.7.8 Username and Password not accepted.");

        var outcome = await _delivery.SendAsync(sender, message);

        Assert.False(outcome.Sent);

        var lines = AuditFor(licence.LicenseId);
        var failure = Assert.Single(lines, a => a.Action == "licence.send-failed");

        Assert.Contains("5.7.8", failure.Note!, StringComparison.Ordinal);
        Assert.Contains("biuro@acme.test", failure.Note!, StringComparison.Ordinal);

        // ⛔ And nothing claims it was sent.
        Assert.DoesNotContain(lines, a => a.Action == "licence.sent");
    }

    /// <summary>⚠ Exactly ONE line per attempt — never both, never neither.</summary>
    [Fact]
    public async Task EveryAttemptWritesExactlyOneLine()
    {
        var (message, _, licence) = Compose();

        await _delivery.SendAsync(FakeEmailSender.Failing(), message);
        await _delivery.SendAsync(FakeEmailSender.Succeeding(), message);

        var delivery = AuditFor(licence.LicenseId)
            .Where(a => a.Action is "licence.sent" or "licence.send-failed")
            .ToList();

        Assert.Equal(2, delivery.Count);
        Assert.Single(delivery, a => a.Action == "licence.sent");
        Assert.Single(delivery, a => a.Action == "licence.send-failed");
    }

    // ── The file route ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>An <c>.eml</c> is an EXPORT, never a send.</b> Nothing has reached the customer when a file
    /// is written, and claiming otherwise would put a false statement in an append-only history.
    /// </summary>
    [Fact]
    public async Task WritingAFileIsRecordedAsAnExportAndNotAsASend()
    {
        var (message, _, licence) = Compose();
        var path = Path.Combine(_manager.Paths.Root, "licence.eml");

        var outcome = await _delivery.ExportAsync(new EmlFileEmailSender(path), message);

        Assert.True(outcome.Sent, outcome.Error);
        Assert.True(File.Exists(path));

        var lines = AuditFor(licence.LicenseId);
        Assert.Contains(lines, a => a.Action == "licence.exported" &&
                                    a.Note!.Contains("licence.eml", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, a => a.Action == "licence.sent");
    }

    /// <summary>⚠ A file that was not written is not an event in the licence's life — same as SaveArtifact.</summary>
    [Fact]
    public async Task AFailedFileWriteRecordsNothing()
    {
        var (message, _, licence) = Compose();

        // A directory cannot be overwritten by a file.
        var path = Path.Combine(_manager.Paths.Root, "as-a-folder");
        Directory.CreateDirectory(path);

        var outcome = await _delivery.ExportAsync(new EmlFileEmailSender(path), message);

        Assert.False(outcome.Sent);
        Assert.DoesNotContain(AuditFor(licence.LicenseId), a => a.Action == "licence.exported");
    }

    public void Dispose() => _manager.Dispose();
}
