using System;
using System.IO;
using System.Text;
using EmberTern.Licensing;
using EmberTern.LicenseManager.Data;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>L3's exit criterion: a licence issued end to end.</b>
///
/// <para>Ceremony → keystore → register → issue → <c>EmberTern.etlic</c> on disk → read back → verified by
/// <c>EmberTern.Licensing</c>, the assembly the customer actually runs. Nothing in this file takes a
/// shortcut the product does not have.</para>
/// </summary>
public sealed class IssuingWorkflowTests : IDisposable
{
    private readonly ManagerFixture _manager = new();

    private LicenseVerificationContext ClientContext(DateTimeOffset? now = null) =>
        new(_manager.Session.TrustedKeys,
            now ?? _manager.Now,
            LicenseConstants.ProductId,
            LicenseConstants.MaxSupportedPayloadVersion,
            LicenseConstants.DefaultGracePeriod,
            BuildReleaseDate: null);

    [Fact]
    public void ALicenceGoesFromTheRegisterToADiskFileToAValidVerdict()
    {
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);

        var result = _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        var path = Path.Combine(_manager.Paths.Root, LicenseConstants.DeliveredFileName);
        _manager.Workflow.SaveArtifact(result.Artifact, path);

        var fromDisk = File.ReadAllText(path);
        var verdict = LicenseVerifier.Verify(fromDisk, ClientContext());

        Assert.Equal(LicenseStatus.Valid, verdict.Status);
        Assert.Equal("ACME Sp. z o.o.", verdict.Payload!.Licensee);
        Assert.Equal(5, verdict.Payload.Seats);
        Assert.Equal(licence.LicenseId, verdict.Payload.LicenseId);
        Assert.Equal("R1", verdict.Payload.KeyId);
    }

    [Fact]
    public void TheSavedFileIsUtf8WithoutAByteOrderMark()
    {
        // ⚠ The project's rule for every generated text file (gotcha #178): Encoding.UTF8 emits a BOM,
        //    which breaks the first statement for anything parsing the file as plain text.
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);
        var result = _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        var path = Path.Combine(_manager.Paths.Root, "out.etlic");
        _manager.Workflow.SaveArtifact(result.Artifact, path);

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.StartsWith(LicenseArmor.BeginMarker, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLicenseeIsTakenFromTheRegisterAtTheMomentOfSigning()
    {
        // ⭐ Not copied into the licence row. A corrected company name must not leave old rows disagreeing
        //    with the artifacts that were actually sent.
        var customer = _manager.SaveCustomer("Old Name Sp. z o.o.");
        var licence = _manager.SaveLicense(customer);

        var renamed = _manager.Register.SaveCustomer(customer with { Name = "New Name S.A." });
        var result = _manager.Workflow.Issue(_manager.Session, licence, renamed, IssueReasons.Initial);

        Assert.Equal("New Name S.A.", result.Issued.Payload.Licensee);
    }

    [Fact]
    public void IssuingRecordsBeforeAnythingReachesTheCustomer()
    {
        // ⭐ Recording is the act that matters; saving a file is a convenience the operator may repeat or
        //    skip. A signed licence the register does not know about is the one state from which it can
        //    no longer answer "what did we send this customer?".
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);

        _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        var artifacts = _manager.Register.GetArtifacts(licence.LicenseId);
        Assert.Single(artifacts);
        Assert.Equal("R1", artifacts[0].KeyId);
        Assert.Contains(_manager.Register.GetAudit(), e => e.Action == "licence.issued");
    }

    [Fact]
    public void TheStoredPayloadIsTheBytesThatWereSigned()
    {
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);

        var result = _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);
        var stored = _manager.Register.GetArtifacts(licence.LicenseId)[0];

        Assert.Equal(
            stored.Token.Split('.')[1],
            LicenseEnvelope.EncodePayload(Encoding.UTF8.GetBytes(stored.PayloadJson)));
        Assert.Equal(result.Issued.Token, stored.Token);
    }

    [Fact]
    public void ReExportingTheStoredArtifactProducesTheSameBytesAndSignsNothingNew()
    {
        // ⭐ "The customer lost their file" is a five-second re-export, not a re-issue with a new iat that
        //    EmberTern would then treat as a replacement (§16.4).
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);
        var result = _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        var first = Path.Combine(_manager.Paths.Root, "a.etlic");
        var second = Path.Combine(_manager.Paths.Root, "b.etlic");
        _manager.Workflow.SaveArtifact(result.Artifact, first);
        _manager.Workflow.SaveArtifact(_manager.Register.GetArtifacts(licence.LicenseId)[0], second);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.Single(_manager.Register.GetArtifacts(licence.LicenseId));
    }

    [Fact]
    public void ARenewalKeepsTheLicenceIdAndMovesTheIssueDate()
    {
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);
        var first = _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        _manager.Now = _manager.Now.AddMonths(11);
        var extended = _manager.Register.SaveLicense(licence with { ExpiresAt = _manager.Now.AddYears(1) });
        var renewal = _manager.Workflow.Issue(_manager.Session, extended, customer, IssueReasons.Renewal);

        Assert.Equal(first.Issued.Payload.LicenseId, renewal.Issued.Payload.LicenseId);
        Assert.True(renewal.Issued.Payload.IssuedAt > first.Issued.Payload.IssuedAt);
        Assert.Equal(2, _manager.Register.GetArtifacts(licence.LicenseId).Count);
    }

    [Fact]
    public void AnExportIsRecordedToo()
    {
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);
        var result = _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        _manager.Workflow.SaveArtifact(result.Artifact, Path.Combine(_manager.Paths.Root, "x.etlic"));

        Assert.Contains(_manager.Register.GetAudit(), e => e.Action == "licence.exported");
    }

    [Fact]
    public void InspectingAnArtifactAnswersWithTheProductsOwnOpinion()
    {
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);
        var result = _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        Assert.Equal(LicenseStatus.Valid, _manager.Workflow.Inspect(_manager.Session, result.Artifact).Status);
    }

    [Fact]
    public void ASecondKeystoreIsRefusedRatherThanOverwritten()
    {
        // ⛔ Overwriting a keystore cannot be undone: every licence in the field was signed by the key it
        //    held, and nothing can renew them afterwards.
        Assert.Throws<InvalidOperationException>(() =>
            EmberTern.LicenseManager.Services.SigningSession.Create(
                _manager.Paths, "R2", "another passphrase entirely", _manager.Now));
    }

    [Fact]
    public void TheKeystoreOnDiskOpensWithItsPassphraseAndNotWithout()
    {
        using var reopened = EmberTern.LicenseManager.Services.SigningSession.Unlock(
            _manager.Paths, ManagerFixture.Passphrase);

        Assert.Equal(_manager.Session.PublicKey, reopened.PublicKey);

        Assert.Throws<EmberTern.Licensing.Issuing.KeyStoreException>(
            () => EmberTern.LicenseManager.Services.SigningSession.Unlock(_manager.Paths, "wrong"));
    }

    public void Dispose() => _manager.Dispose();
}
