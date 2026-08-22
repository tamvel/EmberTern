using System;
using System.IO;
using System.Linq;
using System.Text;
using EmberTern.Licensing;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Services;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>L6.2's exit criterion, and the only guard that can fail for a reason a customer would feel:</b>
/// the licence that leaves in an e-mail is the licence the register stored, and it still verifies after a
/// real MIME round trip.
///
/// <para>Register → composer → <c>MailMessage</c> → an <c>.eml</c> on disk → the attachment read back →
/// <c>LicenseVerifier</c>, the code the customer actually runs. ⛔ Nothing here is mocked: a stub anywhere
/// on that path would hide exactly the defect the path exists to catch.</para>
/// </summary>
public sealed class LicenseMessageArtifactTests : IDisposable
{
    private readonly ManagerFixture _manager = new();

    private static SmtpSettings Settings(string language = MessageLanguages.Polish) => new()
    {
        Host = "smtp.example.test",
        FromAddress = "licencje@example.test",
        FromName = "EmberTern — licencje",
        MessageLanguage = language,
    };

    private LicenseVerificationContext ClientContext() =>
        new(_manager.Session.TrustedKeys,
            _manager.Now,
            LicenseConstants.ProductId,
            LicenseConstants.MaxSupportedPayloadVersion,
            LicenseConstants.DefaultGracePeriod,
            BuildReleaseDate: null);

    private (IssuedArtifactRecord Artifact, CustomerRecord Customer, LicenseRecord Licence) Issue(
        string name = "ACME Sp. z o.o.")
    {
        var customer = _manager.SaveCustomer(name, "biuro@acme.test");
        var licence = _manager.SaveLicense(customer);
        var result = _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);
        return (result.Artifact, customer, licence);
    }

    /// <summary>
    /// ⭐⭐ <b>CONTRACT A — the same bytes, not an equivalent token.</b> The attachment is compared against
    /// what <see cref="IssuingWorkflow.SaveArtifact"/> writes for the same artifact, byte for byte. ⛔ "It
    /// re-composes to something equivalent" is not the assertion: the point is that there is ONE definition
    /// of what <c>EmberTern.etlic</c> contains, and both delivery routes read it.
    /// </summary>
    [Fact]
    public void TheAttachmentIsByteForByteWhatSaveArtifactWrites()
    {
        var (artifact, customer, _) = Issue();

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings());

        var path = Path.Combine(_manager.Paths.Root, LicenseConstants.DeliveredFileName);
        _manager.Workflow.SaveArtifact(artifact, path);

        Assert.Equal(File.ReadAllBytes(path), message.AttachmentBytes);
    }

    /// <summary>⚠ And the file on disk is still UTF-8 without a BOM after the seam moved (gotcha #178).</summary>
    [Fact]
    public void ThoseBytesAreStillArmoredUtf8WithoutAByteOrderMark()
    {
        var (artifact, customer, _) = Issue();

        var bytes = LicenseMessageComposer.Compose(artifact, customer, Settings()).AttachmentBytes;

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.StartsWith(
            LicenseArmor.BeginMarker, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ <b>CONTRACT B — the licence survives transport.</b> Composed, written as MIME, read back out of
    /// the encoded attachment, and verified by <c>EmberTern.Licensing</c> — the assembly the customer runs.
    /// </summary>
    [Fact]
    public void TheAttachedLicenceStillVerifiesAfterAMimeRoundTrip()
    {
        var (artifact, customer, licence) = Issue();

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings());
        var eml = MimeProbe.Write(message, Path.Combine(_manager.Paths.Root, "pickup"));

        var delivered = MimeProbe.Attachment(eml, LicenseConstants.DeliveredFileName);
        Assert.Equal(message.AttachmentBytes, delivered);

        // ⭐ Exactly what the customer does: save the attachment, then hand its TEXT to the product.
        var verdict = LicenseVerifier.Verify(Encoding.UTF8.GetString(delivered), ClientContext());

        Assert.Equal(LicenseStatus.Valid, verdict.Status);
        Assert.Equal("ACME Sp. z o.o.", verdict.Payload!.Licensee);
        Assert.Equal(licence.LicenseId, verdict.Payload.LicenseId);
        Assert.Equal(5, verdict.Payload.Seats);
    }

    /// <summary>
    /// ⭐⭐ <b>CONTRACT C — Polish characters survive the headers.</b> Three headers carry customer or
    /// operator text, and each of them travels as an RFC 2047 encoded word; "it works" means each decodes
    /// back to exactly what was composed.
    /// </summary>
    [Fact]
    public void PolishCharactersSurviveEveryHeaderThatCarriesThem()
    {
        var (artifact, customer, _) = Issue("Żółć Sp. z o.o.");

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings());
        var eml = MimeProbe.Write(message, Path.Combine(_manager.Paths.Root, "pickup"));

        Assert.Equal("Licencja EmberTern — Żółć Sp. z o.o.", MimeProbe.Header(eml, "Subject"));
        Assert.Equal(message.Subject, MimeProbe.Header(eml, "Subject"));

        Assert.Contains("Żółć Sp. z o.o.", MimeProbe.Header(eml, "To"), StringComparison.Ordinal);
        Assert.Contains("biuro@acme.test", MimeProbe.Header(eml, "To"), StringComparison.Ordinal);

        Assert.Contains("EmberTern — licencje", MimeProbe.Header(eml, "From"), StringComparison.Ordinal);
        Assert.Contains("licencje@example.test", MimeProbe.Header(eml, "From"), StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠ The bodies too — a stripped diacritic in the body is the same defect one layer down, and it is
    /// asserted after the transport DECODED them, not on the strings the composer returned.
    /// </summary>
    [Fact]
    public void PolishCharactersSurviveBothBodies()
    {
        var (artifact, customer, _) = Issue("Żółć Sp. z o.o.");

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings());
        var eml = MimeProbe.Write(message, Path.Combine(_manager.Paths.Root, "pickup"));

        Assert.Equal(message.TextBody, MimeProbe.TextBody(eml));
        Assert.Equal(message.HtmlBody, MimeProbe.HtmlBody(eml));

        Assert.Contains("Żółć Sp. z o.o.", MimeProbe.TextBody(eml), StringComparison.Ordinal);
        Assert.Contains("Żółć Sp. z o.o.", MimeProbe.HtmlBody(eml), StringComparison.Ordinal);
        Assert.Contains("Ważna do", MimeProbe.TextBody(eml), StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ <b>The artifact comes from the register's POINTER.</b> After a re-issue the licence has two
    /// artifacts, and the one the customer must receive is whichever <c>license_current_artifact</c> names —
    /// the same authority <c>InspectLatest</c> reads. ⛔ Never <c>Artifacts[0]</c>, and never a new signature.
    /// </summary>
    [Fact]
    public void ComposingFromTheRegistersCurrentPointerAttachesTheCurrentArtifact()
    {
        var (first, customer, licence) = Issue();

        _manager.Now = _manager.Now.AddDays(1);
        var second = _manager.Workflow.Issue(
            _manager.Session, licence, customer, IssueReasons.ReissueLost);

        var current = _manager.Register.GetCurrentArtifact(licence.LicenseId)!;
        Assert.Equal(second.Artifact.ArtifactId, current.ArtifactId);
        Assert.NotEqual(first.ArtifactId, current.ArtifactId);

        var message = LicenseMessageComposer.Compose(current, customer, Settings());

        Assert.Equal(IssuingWorkflow.ArtifactBytes(second.Artifact), message.AttachmentBytes);
        Assert.NotEqual(IssuingWorkflow.ArtifactBytes(first), message.AttachmentBytes);

        // ⭐ And nothing new was signed on the way: the register still holds exactly two artifacts.
        Assert.Equal(2, _manager.Register.GetArtifacts(licence.LicenseId).Count);
    }

    public void Dispose() => _manager.Dispose();
}
