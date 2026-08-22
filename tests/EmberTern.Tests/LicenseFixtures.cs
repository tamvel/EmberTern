using System;
using System.Security.Cryptography;
using EmberTern.Licensing;

namespace EmberTern.Tests;

/// <summary>
/// Mints real, correctly signed licences for the L4 tests.
///
/// <para>⭐⭐ <b>It signs here, in the test, rather than calling the issuer — and that is a constraint of
/// the architecture rather than an inconvenience.</b> <c>EmberTern.Licensing.Issuing</c> is deliberately
/// absent from <c>EmberTern.slnx</c>, so no project in this solution — including this test project — can
/// reference it. That is exactly what <c>PrivateKeyNeverShipsTests</c> exists to keep true. Signing with a
/// throwaway key over the public envelope API costs a dozen lines and keeps the boundary intact.</para>
///
/// <para>⭐ The key is generated per fixture and never leaves memory, so nothing here is a key that could
/// sign anything a customer would see. ⛔ These keys are deliberately NOT the shipped one: since the
/// 2026-08-22 ceremony <c>TrustedKeys.Production</c> carries <c>R1</c>, and a test must never depend on
/// the production key — the service takes a table as a constructor parameter for precisely this.</para>
/// </summary>
internal sealed class LicenseFixtures : IDisposable
{
    internal const string KeyId = "T1";
    internal const string Licensee = "ACME Sp. z o.o.";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The table a <c>LicenseService</c> under test verifies against.</summary>
    internal TrustedKeyTable TrustedKeys => new(
    [
        new TrustedKey(KeyId, SignatureAlgorithm.EcdsaP256Sha256, _key.ExportSubjectPublicKeyInfo(), Revoked: false),
    ]);

    /// <summary>A table that knows a DIFFERENT key — everything signed here then reads as an unknown issuer.</summary>
    internal static TrustedKeyTable Foreign
    {
        get
        {
            using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return new TrustedKeyTable(
            [
                new TrustedKey(KeyId, SignatureAlgorithm.EcdsaP256Sha256, other.ExportSubjectPublicKeyInfo(), false),
            ]);
        }
    }

    /// <summary>Builds and signs a licence, armoured exactly as the delivered file is.</summary>
    internal string Issue(
        DateTimeOffset issuedAt,
        DateTimeOffset notBefore,
        DateTimeOffset expiresAt,
        string? licenseId = null,
        string? licensee = null,
        string product = LicenseConstants.ProductId,
        DateTimeOffset? maintenanceUntil = null,
        int seats = 5)
    {
        var payload = new LicensePayload
        {
            Version = LicenseConstants.MaxSupportedPayloadVersion,
            KeyId = KeyId,
            AlgorithmId = SignatureAlgorithmIds.EcdsaP256Sha256,
            LicenseId = licenseId ?? "lid-fixture-0001",
            Product = product,
            Licensee = licensee ?? Licensee,
            Seats = seats,
            IssuedAt = issuedAt,
            NotBefore = notBefore,
            ExpiresAt = expiresAt,
            MaintenanceUntil = maintenanceUntil,
        };

        var segment = LicenseEnvelope.EncodePayload(payload.WriteJson());
        var signature = _key.SignData(LicenseEnvelope.BuildSigningInput(segment), HashAlgorithmName.SHA256);

        return LicenseArmor.Wrap(LicenseEnvelope.Compose(segment, signature));
    }

    /// <summary>A licence valid for a year around <paramref name="now"/>.</summary>
    internal string Valid(DateTimeOffset now, string? licenseId = null) =>
        Issue(now, now.AddDays(-1), now.AddYears(1), licenseId);

    /// <summary>
    /// Alters one character of the signed payload, leaving a well-formed artifact whose signature no
    /// longer matches.
    ///
    /// <para>⚠⚠ <b>Written after the obvious version of the tampering test proved nothing.</b> It did a text
    /// <c>Replace</c> of the licensee's name on the armoured licence — but the payload is base64url, so the
    /// name does not appear verbatim anywhere in the file. The replace matched nothing, mutated nothing, and
    /// the "altered" licence verified perfectly. ⭐ Identical in shape to the keystore finding in L2 (§35.3):
    /// <b>editing an encoded artifact as text silently edits nothing</b>, and the test then reports the
    /// absence of a failure as a success.</para>
    ///
    /// <para>⭐ So this mutates the ENCODING — one character inside the payload segment, swapped for another
    /// legal base64url character — which is what an actually-modified licence looks like.</para>
    /// </summary>
    internal static string Tamper(string licence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licence);

        var magic = licence.IndexOf(LicenseEnvelope.Magic + ".", StringComparison.Ordinal);
        Assert(magic >= 0, "The fixture did not produce an ETL1 artifact.");

        var target = magic + LicenseEnvelope.Magic.Length + 6;
        Assert(target < licence.Length, "The artifact is too short to tamper with.");

        var replacement = licence[target] == 'A' ? 'B' : 'A';
        return licence[..target] + replacement + licence[(target + 1)..];
    }

    private static void Assert(bool condition, string because)
    {
        if (!condition)
        {
            throw new InvalidOperationException(because);
        }
    }

    public void Dispose() => _key.Dispose();
}
