using System;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The accepting half of <see cref="LicenseVerifier"/> — every state a good licence can be in.
/// The refusing half is <see cref="LicenseTamperCorpusTests"/>.
/// </summary>
public sealed class LicenseVerifierTests : IDisposable
{
    private readonly LicenseTestFactory _factory = new();

    private static DateTimeOffset Expiry => LicenseTestFactory.DefaultPayload.ExpiresAt;

    [Fact]
    public void AGoodLicenceIsValid()
    {
        var verdict = _factory.Verdict();

        Assert.Equal(LicenseStatus.Valid, verdict.Status);
        Assert.Equal(LicenseFailure.None, verdict.Failure);
        Assert.True(verdict.IsUsable);
        Assert.Equal("ACME Sp. z o.o.", verdict.Payload!.Licensee);
        Assert.Equal(5, verdict.Payload.Seats);
    }

    [Fact]
    public void AnArmoredArtifactVerifiesJustLikeABareToken()
    {
        var token = _factory.Sign(LicenseTestFactory.DefaultPayload);

        Assert.Equal(
            LicenseVerifier.Verify(token, _factory.Context()).Status,
            LicenseVerifier.Verify(LicenseArmor.Wrap(token), _factory.Context()).Status);
    }

    [Fact]
    public void BeforeNotBeforeItIsNotYetValid()
    {
        var verdict = _factory.Verdict(now: new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(LicenseStatus.NotYetValid, verdict.Status);
        Assert.False(verdict.IsUsable);
        Assert.NotNull(verdict.Payload);
    }

    [Fact]
    public void PastExpiryItIsUsableThroughoutTheGracePeriod()
    {
        // ⭐ Grace is a correctness requirement, not generosity: renewal in V1 is a human process, and an
        //    expiry that bricks the tool at midnight turns a purchase-order delay into a work stoppage.
        Assert.Equal(LicenseStatus.Grace, _factory.Verdict(now: Expiry.AddSeconds(1)).Status);
        Assert.Equal(LicenseStatus.Grace, _factory.Verdict(now: Expiry.AddDays(7)).Status);
        Assert.True(_factory.Verdict(now: Expiry.AddDays(7)).IsUsable);
    }

    [Fact]
    public void TheGraceBoundaryIsExactAndInclusive()
    {
        var lastMoment = Expiry + LicenseConstants.DefaultGracePeriod;

        Assert.Equal(LicenseStatus.Grace, _factory.Verdict(now: lastMoment).Status);
        Assert.Equal(LicenseStatus.Expired, _factory.Verdict(now: lastMoment.AddSeconds(1)).Status);
    }

    [Fact]
    public void AnExpiredLicenceStillHandsUpItsPayload()
    {
        // ⭐ The UI must be able to say "expired on <date>, licensed to <name>". Withholding the payload
        //    here would force the message to be generic exactly where the user needs a fact.
        var verdict = _factory.Verdict(now: Expiry.AddYears(1));

        Assert.Equal(LicenseStatus.Expired, verdict.Status);
        Assert.False(verdict.IsUsable);
        Assert.Equal(Expiry, verdict.Payload!.ExpiresAt);
    }

    [Fact]
    public void ARefusedArtifactNeverHandsUpItsContents()
    {
        // ⛔ Showing the licensee name off an unverified file would be showing the user whatever the
        //    file's author chose to write there.
        var forged = _factory.SignWithForeignKey(LicenseTestFactory.DefaultPayload);
        var verdict = LicenseVerifier.Verify(forged, _factory.Context());

        Assert.Equal(LicenseStatus.Invalid, verdict.Status);
        Assert.Null(verdict.Payload);
    }

    [Fact]
    public void MaintenanceGatesABuildReleasedAfterItsDate()
    {
        var maintenance = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var payload = LicenseTestFactory.DefaultPayload with { MaintenanceUntil = maintenance };
        var token = _factory.Sign(payload);

        Assert.Equal(
            LicenseStatus.VersionNotCovered,
            LicenseVerifier.Verify(token, _factory.Context(buildReleaseDate: maintenance.AddDays(1))).Status);

        Assert.Equal(
            LicenseStatus.Valid,
            LicenseVerifier.Verify(token, _factory.Context(buildReleaseDate: maintenance.AddDays(-1))).Status);
    }

    [Fact]
    public void MaintenanceIsInertWhenTheHostDoesNotDeclareABuildDate()
    {
        // The License Manager inspects licences rather than being licensed by one, so it passes null.
        var payload = LicenseTestFactory.DefaultPayload with
        {
            MaintenanceUntil = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.Equal(
            LicenseStatus.Valid,
            LicenseVerifier.Verify(_factory.Sign(payload), _factory.Context(buildReleaseDate: null)).Status);
    }

    [Fact]
    public void NoLicenceIsAvailableAsAVerdictTheHostCanProduce()
    {
        Assert.Equal(LicenseStatus.Unlicensed, LicenseVerdict.Unlicensed.Status);
        Assert.Equal(LicenseFailure.FileMissing, LicenseVerdict.Unlicensed.Failure);
        Assert.False(LicenseVerdict.Unlicensed.IsUsable);
    }

    [Fact]
    public void OnlyValidAndGraceAreUsable()
    {
        Assert.True(_factory.Verdict().IsUsable);
        Assert.True(_factory.Verdict(now: Expiry.AddDays(1)).IsUsable);
        Assert.False(_factory.Verdict(now: Expiry.AddYears(1)).IsUsable);
        Assert.False(_factory.Verdict(now: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)).IsUsable);
    }

    [Fact]
    public void TheShippedTrustedKeyTableIsStillEmptyAtThisStage()
    {
        // ⚠ Correct for L1 — the key ceremony is L2 (§24.1) and the public key is recorded in L7. This
        //    test is a REMINDER, not a rule: when L2 adds the first key, update it to assert the key is
        //    present, non-revoked and P-256. ⛔ Do not delete it — an empty production table shipping by
        //    accident would refuse every licence in the field with "unknown key".
        Assert.Empty(TrustedKeys.Production.Keys);
    }

    [Fact]
    public void ATrustedKeyTableRefusesAKeyItCannotUse()
    {
        // ⭐ Validated at construction so the verification path has no "our own key is broken" branch —
        //    that is a programming error in OUR table, and reporting it to a user as an invalid licence
        //    would send them chasing a file that is perfectly fine.
        Assert.Throws<ArgumentException>(() => new TrustedKeyTable(
        [
            new TrustedKey("X", SignatureAlgorithm.EcdsaP256Sha256, [1, 2, 3]),
        ]));

        Assert.Throws<ArgumentException>(() => new TrustedKeyTable(
        [
            new TrustedKey("X", SignatureAlgorithm.EcdsaP256Sha256, ValidPublicKey()),
            new TrustedKey("X", SignatureAlgorithm.EcdsaP256Sha256, ValidPublicKey()),
        ]));
    }

    private static byte[] ValidPublicKey()
    {
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportSubjectPublicKeyInfo();
    }

    public void Dispose() => _factory.Dispose();
}
