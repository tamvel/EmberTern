using System;
using System.Security.Cryptography;
using System.Text;
using EmberTern.Licensing;

namespace EmberTern.Tests;

/// <summary>
/// Builds real, really-signed licences for the licensing tests.
///
/// <para>⭐ It signs with a throwaway P‑256 key created here, using <see cref="ECDsa"/> directly — NOT with
/// the product's issuing code, which does not exist until stage L2. That is deliberate and it stays that
/// way: a verifier proved against its own signer proves only that the two agree. Proving it against the
/// platform's ECDSA proves it agrees with the algorithm.</para>
///
/// <para>⛔ Nothing here ever touches <see cref="TrustedKeys.Production"/>, and that rule OUTLIVED the
/// reason it was written: the shipped table used to be empty, and is not since the 2026-08-22 ceremony.
/// A test signing with the production key would be a test that cannot run without it.</para>
/// </summary>
internal sealed class LicenseTestFactory : IDisposable
{
    internal const string DefaultKeyId = "T1";
    internal const string OtherKeyId = "T2";
    internal const string RevokedKeyId = "TR";

    private readonly ECDsa _key;
    private readonly ECDsa _otherKey;

    internal LicenseTestFactory()
    {
        _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Keys = new TrustedKeyTable(
        [
            new TrustedKey(
                DefaultKeyId, SignatureAlgorithm.EcdsaP256Sha256, _key.ExportSubjectPublicKeyInfo()),
            new TrustedKey(
                RevokedKeyId, SignatureAlgorithm.EcdsaP256Sha256, _key.ExportSubjectPublicKeyInfo(),
                Revoked: true),
        ]);
    }

    /// <summary>The table the tests verify against: one good key, one revoked key. ⛔ No <c>T2</c>.</summary>
    internal TrustedKeyTable Keys { get; }

    /// <summary>The instant every default context treats as "now" — comfortably inside the default licence.</summary>
    internal static DateTimeOffset Now { get; } = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A well-formed, currently valid payload. Mutate a copy with <c>with</c>.</summary>
    internal static LicensePayload DefaultPayload { get; } = new()
    {
        Version = 1,
        KeyId = DefaultKeyId,
        AlgorithmId = SignatureAlgorithmIds.EcdsaP256Sha256,
        LicenseId = "0191f3c4b2a741d89e0fa21c7d4e3056",
        Product = LicenseConstants.ProductId,
        Licensee = "ACME Sp. z o.o.",
        Seats = 5,
        IssuedAt = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero),
        NotBefore = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2027, 8, 15, 23, 59, 59, TimeSpan.Zero),
    };

    /// <summary>A context using this factory's keys.</summary>
    internal LicenseVerificationContext Context(
        DateTimeOffset? now = null,
        DateTimeOffset? buildReleaseDate = null,
        int? maxSupportedVersion = null) =>
        new(Keys,
            now ?? Now,
            LicenseConstants.ProductId,
            maxSupportedVersion ?? LicenseConstants.MaxSupportedPayloadVersion,
            LicenseConstants.DefaultGracePeriod,
            buildReleaseDate);

    /// <summary>Signs a payload into a bare token.</summary>
    internal string Sign(LicensePayload payload) => SignJson(payload.WriteJson());

    /// <summary>Signs a payload and verifies it in one step — the common shape of an accepting test.</summary>
    internal LicenseVerdict Verdict(
        LicensePayload? payload = null,
        DateTimeOffset? now = null,
        DateTimeOffset? buildReleaseDate = null) =>
        LicenseVerifier.Verify(Sign(payload ?? DefaultPayload), Context(now, buildReleaseDate));

    /// <summary>Signs arbitrary bytes as the payload — the corpus uses this to sign malformed JSON.</summary>
    internal string SignJson(byte[] payloadJson)
    {
        var segment = LicenseEnvelope.EncodePayload(payloadJson);
        var signature = _key.SignData(
            LicenseEnvelope.BuildSigningInput(segment),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return LicenseEnvelope.Compose(segment, signature);
    }

    /// <summary>Signs arbitrary JSON text as the payload.</summary>
    internal string SignJson(string payloadJson) => SignJson(Encoding.UTF8.GetBytes(payloadJson));

    /// <summary>Signs with a key the table does not know — the "forged licence" case.</summary>
    internal string SignWithForeignKey(LicensePayload payload)
    {
        var segment = LicenseEnvelope.EncodePayload(payload.WriteJson());
        var signature = _otherKey.SignData(
            LicenseEnvelope.BuildSigningInput(segment),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return LicenseEnvelope.Compose(segment, signature);
    }

    /// <summary>Signs with the right key but in DER — the wrong signature encoding (§15.1 caveat 1).</summary>
    internal string SignDer(LicensePayload payload)
    {
        var segment = LicenseEnvelope.EncodePayload(payload.WriteJson());
        var signature = _key.SignData(
            LicenseEnvelope.BuildSigningInput(segment),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

        return LicenseEnvelope.Compose(segment, signature);
    }

    /// <summary>Replaces a token's signature segment, keeping its payload.</summary>
    internal static string WithSignature(string token, ReadOnlySpan<byte> signature)
    {
        var parts = token.Split('.');
        return LicenseEnvelope.Compose(parts[1], signature);
    }

    /// <summary>The raw signature bytes of a token.</summary>
    internal static byte[] SignatureOf(string token)
    {
        var segment = token.Split('.')[2];
        var padded = segment.Replace('-', '+').Replace('_', '/')
            .PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    /// <summary>The payload segment of a token.</summary>
    internal static string PayloadSegmentOf(string token) => token.Split('.')[1];

    public void Dispose()
    {
        _key.Dispose();
        _otherKey.Dispose();
    }
}
