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

    /// <summary>
    /// ⭐⭐ The shipped table carries exactly the key the ceremony produced — and it is the SAME key the
    /// ceremony record names.
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <b>This replaces <c>TheShippedTrustedKeyTableIsStillEmptyAtThisStage</c>, in the same
    /// change that added the key.</b> That test was right for five stages and load-bearing: an empty
    /// production table shipping by accident refuses every licence in the field with "unknown key". But it
    /// pinned a deliberately TEMPORARY state, and on the day that state ended it would have gone red
    /// reading *"you broke the key table"* rather than *"this promise expired"* — gotcha #407. A guard over
    /// an interim state is retired WITH the decision that ends it, never after.</para>
    ///
    /// <para>⭐ It asserts BOTH halves, because either alone passes on a broken key: the table's SHAPE (one
    /// entry, the right id, not revoked, a usable P-256 key) and its IDENTITY (the fingerprint recorded by
    /// the ceremony). ⛔ Not <c>Assert.NotEmpty</c> — that passes for ANY key, including a development one
    /// pasted by mistake, which is the single most expensive mistake available here.</para>
    ///
    /// <para>⚠ The fingerprint is recomputed rather than read from <c>KeyCeremony.Fingerprint</c>: that type
    /// lives in <c>EmberTern.Licensing.Issuing</c>, which this solution cannot reference and must never be
    /// able to — see <c>PrivateKeyNeverShipsTests</c>. ⭐ So this is a second, independent implementation of
    /// the same definition (SHA-256 over the DER SubjectPublicKeyInfo), which is a property rather than a
    /// duplication: a mistake in one is not mirrored in the other.</para>
    /// </remarks>
    [Fact]
    public void TheShippedTrustedKeyTableCarriesTheCeremonyKey()
    {
        // ⭐ The ceremony record: docs/design/licensing-system.md §35.4, R1, 2026-08-22.
        //   ⚠ A LITERAL on purpose. It is the one place the pasted key is checked against the value written
        //     down independently, so a transcription error fails the build instead of a customer's licence.
        const string RecordedFingerprint =
            "B55DCB8FAB7AD12EB77F798B89A59B5722AA11CAD71F27BE9DD49C7CFC0905AD";

        var key = Assert.Single(TrustedKeys.Production.Keys);

        Assert.Equal("R1", key.KeyId);
        Assert.False(key.Revoked);
        Assert.Equal(SignatureAlgorithm.EcdsaP256Sha256, key.Algorithm);

        // The table validates its entries at construction, so reaching this line already proves the key
        // imports. Asserting the curve as well says WHICH key would have to change for that to stop.
        using (var ecdsa = System.Security.Cryptography.ECDsa.Create())
        {
            ecdsa.ImportSubjectPublicKeyInfo(key.SubjectPublicKeyInfo, out var read);

            Assert.Equal(key.SubjectPublicKeyInfo.Length, read);
            Assert.Equal(256, ecdsa.KeySize);
        }

        Assert.Equal(
            RecordedFingerprint,
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(key.SubjectPublicKeyInfo)));

        // ⛔ And the lookup answers for that id and for nothing else — there is no "try every key" path.
        Assert.True(TrustedKeys.Production.TryGet("R1", out _));
        Assert.False(TrustedKeys.Production.TryGet("R2", out _));
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
