using System;
using System.Linq;
using System.Security.Cryptography;
using EmberTern.Licensing;
using EmberTern.Licensing.Issuing;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// Issuing, and the thing L2 exists to prove: ⭐ <b>an artifact signed by
/// <c>EmberTern.Licensing.Issuing</c> verifies in <c>EmberTern.Licensing</c></b> — the assembly the
/// customer actually runs, through the same public entry point EmberTern will call.
///
/// <para>Every test here crosses that boundary. Nothing in this file uses a shortcut the product does not
/// have.</para>
/// </summary>
public sealed class LicenseIssuerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly KeyStoreEntry _entry = KeyStoreEntry.Generate("R1", Now);
    private readonly IssuingKey _key;
    private readonly LicenseIssuer _issuer;

    public LicenseIssuerTests()
    {
        using var store = KeyStore.Open(KeyStore.Create([_entry], "a passphrase for the tests"),
            "a passphrase for the tests");
        _key = store.Unlock("R1");
        _issuer = new LicenseIssuer(_key);
    }

    private static LicenseTerms Terms => new()
    {
        Licensee = "ACME Sp. z o.o.",
        Seats = 5,
        NotBefore = Now,
        ExpiresAt = Now.AddYears(1),
    };

    private LicenseVerificationContext ClientContext(DateTimeOffset? now = null) =>
        new(new TrustedKeyTable([new TrustedKey("R1", SignatureAlgorithm.EcdsaP256Sha256, _key.ExportPublicKey())]),
            now ?? Now,
            LicenseConstants.ProductId,
            LicenseConstants.MaxSupportedPayloadVersion,
            LicenseConstants.DefaultGracePeriod,
            BuildReleaseDate: null);

    [Fact]
    public void AnIssuedLicenceVerifiesInTheClientAssembly()
    {
        var issued = _issuer.Issue(Terms, Now);

        var verdict = LicenseVerifier.Verify(issued.ArmoredText, ClientContext());

        Assert.Equal(LicenseStatus.Valid, verdict.Status);
        Assert.Equal("ACME Sp. z o.o.", verdict.Payload!.Licensee);
        Assert.Equal(5, verdict.Payload.Seats);
        Assert.Equal("R1", verdict.Payload.KeyId);
        Assert.Equal(LicenseConstants.ProductId, verdict.Payload.Product);
    }

    [Fact]
    public void TheArmoredFormIsWhatGetsWrittenToDisk()
    {
        var issued = _issuer.Issue(Terms, Now);

        Assert.StartsWith(LicenseArmor.BeginMarker, issued.ArmoredText, StringComparison.Ordinal);
        Assert.Contains(issued.Token[..40], issued.ArmoredText.Replace("\r\n", string.Empty), StringComparison.Ordinal);

        // Both forms are accepted by the client, because a customer may paste either.
        Assert.Equal(
            LicenseVerifier.Verify(issued.Token, ClientContext()).Status,
            LicenseVerifier.Verify(issued.ArmoredText, ClientContext()).Status);
    }

    [Fact]
    public void ThePayloadBytesRecordedAreExactlyTheBytesSigned()
    {
        // ⭐ The register stores PayloadJson verbatim so it can always answer "what exactly did we send
        //    this customer?". If these bytes were a re-serialisation, that answer would be a reconstruction.
        var issued = _issuer.Issue(Terms, Now);

        var segment = issued.Token.Split('.')[1];
        Assert.Equal(segment, LicenseEnvelope.EncodePayload(issued.PayloadJson));
    }

    [Fact]
    public void APostDatedLicenceIsIssuableAndReportsItself()
    {
        // ⚠ The self-check asserts the artifact AUTHENTICATES, not that it is currently Valid — demanding
        //    Valid would make a licence that starts next month unissuable.
        var issued = _issuer.Issue(Terms with { NotBefore = Now.AddMonths(1), ExpiresAt = Now.AddYears(1) }, Now);

        Assert.Equal(LicenseStatus.NotYetValid, LicenseVerifier.Verify(issued.ArmoredText, ClientContext()).Status);
        Assert.Equal(LicenseStatus.Valid,
            LicenseVerifier.Verify(issued.ArmoredText, ClientContext(Now.AddMonths(2))).Status);
    }

    [Fact]
    public void ARenewalKeepsTheLicenceIdAndMovesTheIssueDateForward()
    {
        // ⭐ Same lid + later iat is exactly the pair EmberTern requires before it will replace a stored
        //    licence (§16.4). A renewal that generated a fresh lid would be refused as a different licence.
        var original = _issuer.Issue(Terms, Now);

        var renewal = _issuer.Issue(
            Terms with { LicenseId = original.Payload.LicenseId, ExpiresAt = Now.AddYears(2) },
            Now.AddMonths(11));

        Assert.Equal(original.Payload.LicenseId, renewal.Payload.LicenseId);
        Assert.True(renewal.Payload.IssuedAt > original.Payload.IssuedAt);
        Assert.True(renewal.Payload.ExpiresAt > original.Payload.ExpiresAt);
        Assert.Equal(LicenseStatus.Valid,
            LicenseVerifier.Verify(renewal.ArmoredText, ClientContext(Now.AddYears(1).AddDays(30))).Status);
    }

    [Fact]
    public void LicenceIdsAreUniqueAndOneHundredAndTwentyEightBits()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => LicenseIssuer.NewLicenseId()).ToList();

        Assert.Equal(200, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Equal(32, id.Length));
        Assert.All(ids, id => Assert.True(id.All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'))));
    }

    [Fact]
    public void MaintenanceTravelsWhenSetAndIsAbsentWhenNot()
    {
        Assert.Null(_issuer.Issue(Terms, Now).Payload.MaintenanceUntil);

        var covered = Now.AddYears(1);
        var issued = _issuer.Issue(Terms with { MaintenanceUntil = covered }, Now);

        Assert.Equal(covered, issued.Payload.MaintenanceUntil);
        Assert.Equal(LicenseStatus.VersionNotCovered, LicenseVerifier.Verify(
            issued.ArmoredText,
            ClientContext() with { BuildReleaseDate = covered.AddDays(1) }).Status);
    }

    [Fact]
    public void TimestampsAreTruncatedToWholeSeconds()
    {
        // The wire format carries whole seconds, so truncating here stops a millisecond vanishing silently
        // between what the register records and what the artifact says.
        var issued = _issuer.Issue(Terms, Now.AddMilliseconds(750));

        Assert.Equal(0, issued.Payload.IssuedAt.Millisecond);
        Assert.Equal(Now, issued.Payload.IssuedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ALicenceWithoutALicenseeCannotBeIssued(string licensee)
    {
        Assert.Throws<ArgumentException>(() => _issuer.Issue(Terms with { Licensee = licensee }, Now));
    }

    [Fact]
    public void NonsensicalTermsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => _issuer.Issue(Terms with { Seats = 0 }, Now));
        Assert.Throws<ArgumentException>(() => _issuer.Issue(Terms with { Seats = -3 }, Now));
        Assert.Throws<ArgumentException>(() => _issuer.Issue(
            Terms with { NotBefore = Now, ExpiresAt = Now }, Now));
        Assert.Throws<ArgumentException>(() => _issuer.Issue(
            Terms with { NotBefore = Now.AddDays(2), ExpiresAt = Now.AddDays(1) }, Now));
        Assert.Throws<ArgumentException>(() => _issuer.Issue(Terms with { Product = " " }, Now));
        Assert.Throws<ArgumentException>(() => _issuer.Issue(Terms with { LicenseId = string.Empty }, Now));
    }

    [Fact]
    public void ALicenceSignedByAnotherKeyDoesNotVerifyHere()
    {
        // The other half of the isolation story: possessing the issuer proves nothing without THE key.
        var otherEntry = KeyStoreEntry.Generate("R1", Now);
        using var otherStore = KeyStore.Open(KeyStore.Create([otherEntry], "another"), "another");
        using var otherKey = otherStore.Unlock("R1");

        var foreign = new LicenseIssuer(otherKey).Issue(Terms, Now);

        var verdict = LicenseVerifier.Verify(foreign.ArmoredText, ClientContext());
        Assert.Equal(LicenseStatus.Invalid, verdict.Status);
        Assert.Equal(LicenseFailure.SignatureInvalid, verdict.Failure);
    }

    [Fact]
    public void TheIssuedSignatureIsFixedLengthP1363()
    {
        // ⛔ Never DER. The verifier refuses by length before it asks the provider, so a DER signature
        //    would be refused as SignatureInvalid — a confusing failure for something we produced.
        var issued = _issuer.Issue(Terms, Now);
        var signature = issued.Token.Split('.')[2];

        // 64 raw bytes → 86 unpadded base64url characters.
        Assert.Equal(86, signature.Length);
        Assert.DoesNotContain('=', signature);
    }

    [Fact]
    public void SigningIsNotReachableWithoutGoingThroughTheIssuer()
    {
        // ⭐ IssuingKey.Sign is internal on purpose: a public Sign(byte[]) would be a signing oracle
        //    wearing a helpful name. Every signature this system emits therefore passes the validation
        //    and the self-verification in LicenseIssuer.Issue.
        var signing = typeof(IssuingKey)
            .GetMethods(System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => m.Name.Contains("Sign", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(signing);
    }

    [Fact]
    public void NoPublicApiHandsOutPrivateKeyMaterial()
    {
        var offenders = typeof(KeyStoreEntry).Assembly.GetExportedTypes()
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public |
                                          System.Reflection.BindingFlags.Instance |
                                          System.Reflection.BindingFlags.Static |
                                          System.Reflection.BindingFlags.DeclaredOnly)
                .Select(m => $"{t.Name}.{m.Name}"))
            .Where(name => name.Contains("PrivateKey", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    public void Dispose() => _key.Dispose();
}
