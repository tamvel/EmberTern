using System;
using System.Security.Cryptography;

namespace EmberTern.Licensing;

/// <summary>
/// ⭐ <b>THE one entry point.</b> Bytes in, a <see cref="LicenseVerdict"/> out — no I/O, no clock, no
/// configuration, no network. Everything it depends on arrives in a
/// <see cref="LicenseVerificationContext"/>.
///
/// <para>⚠ <b>This class tells the truth in every build configuration.</b> The Debug/Release distinction
/// (decision D15) lives in the host's <c>LicensingPolicy</c> and decides only whether an unusable verdict
/// <i>blocks</i> the application. It must never move in here: the test suite runs in Debug, so a bypass on
/// this path would make the entire tamper corpus vacuous — every licensing test green, proving nothing.</para>
/// </summary>
public static class LicenseVerifier
{
    private const int EcdsaP256SignatureLength = 64;

    /// <summary>
    /// Runs the full chain of §4, in that order.
    ///
    /// <para>⚠ <b>On "verify first, parse second".</b> The payload must be read before the signature can
    /// be checked, because the <c>kid</c> that selects the key is inside it — that is unavoidable in any
    /// signed-token format. What the rule actually forbids is <i>trusting</i> or <i>re-serialising</i>:
    /// the signature is computed over the encoded segment exactly as it arrived, only <c>kid</c> and
    /// <c>lv</c> are consulted beforehand (and only to choose a key or refuse outright), and no field is
    /// acted on until the signature has verified.</para>
    /// </summary>
    public static LicenseVerdict Verify(string? licenseText, LicenseVerificationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!LicenseArmor.TryUnwrap(licenseText, out var token, out var armorFailure))
        {
            return LicenseVerdict.Invalid(armorFailure);
        }

        if (!LicenseEnvelope.TryParse(token, out var envelope, out var envelopeFailure))
        {
            return LicenseVerdict.Invalid(envelopeFailure);
        }

        if (!LicensePayload.TryParse(envelope.PayloadJson, out var payload, out var payloadDetail))
        {
            return LicenseVerdict.Invalid(LicenseFailure.MalformedPayload, payloadDetail);
        }

        // ── Refused before any signature check, and safe to do so: we are not believing the artifact,
        //    we are declining to interpret it. A licence from a newer EmberTern deserves "update
        //    EmberTern" rather than "invalid licence", and §13.4 rule 2 makes ignoring an unknown high
        //    version the one thing we must never do.
        if (payload.Version > context.MaxSupportedVersion)
        {
            return LicenseVerdict.Invalid(
                LicenseFailure.UnsupportedVersion, payload.Version.ToString());
        }

        if (!context.Keys.TryGet(payload.KeyId, out var key))
        {
            return LicenseVerdict.Invalid(LicenseFailure.UnknownKey, payload.KeyId);
        }

        if (key.Revoked)
        {
            return LicenseVerdict.Invalid(LicenseFailure.RevokedKey, payload.KeyId);
        }

        // ⭐ The TABLE decides the algorithm. payload.AlgorithmId is only allowed to agree or be refused;
        //    it never selects. This is the JWT lesson, and it is why there is no "none" to name.
        if (!string.Equals(
                payload.AlgorithmId, SignatureAlgorithmIds.ToWireId(key.Algorithm), StringComparison.Ordinal))
        {
            return LicenseVerdict.Invalid(LicenseFailure.AlgorithmMismatch, payload.AlgorithmId);
        }

        if (!VerifySignature(key, envelope))
        {
            return LicenseVerdict.Invalid(LicenseFailure.SignatureInvalid);
        }

        // ══ Everything above this line was untrusted. Everything below it is authenticated. ══

        if (!string.Equals(payload.Product, context.ExpectedProduct, StringComparison.Ordinal))
        {
            return LicenseVerdict.Invalid(LicenseFailure.WrongProduct, payload.Product);
        }

        if (context.Now < payload.NotBefore)
        {
            return LicenseVerdict.Accepted(LicenseStatus.NotYetValid, payload);
        }

        if (context.Now > payload.ExpiresAt + context.GracePeriod)
        {
            return LicenseVerdict.Accepted(LicenseStatus.Expired, payload);
        }

        // The maintenance gate runs before Grace is reported, so a build outside maintenance says so
        // rather than telling the user to renew a licence that would not cover this build anyway.
        if (payload.MaintenanceUntil is { } maintenance &&
            context.BuildReleaseDate is { } releaseDate &&
            releaseDate > maintenance)
        {
            return LicenseVerdict.Accepted(LicenseStatus.VersionNotCovered, payload);
        }

        return context.Now > payload.ExpiresAt
            ? LicenseVerdict.Accepted(LicenseStatus.Grace, payload)
            : LicenseVerdict.Accepted(LicenseStatus.Valid, payload);
    }

    private static bool VerifySignature(TrustedKey key, LicenseEnvelope envelope)
    {
        switch (key.Algorithm)
        {
            case SignatureAlgorithm.EcdsaP256Sha256:
                // ⭐ A fixed-length check before the cryptographic one. P1363 is exactly r‖s; a DER
                //    signature, a truncated one or a padded one is refused here rather than relying on
                //    the provider to have the same opinion on every platform.
                if (envelope.Signature.Length != EcdsaP256SignatureLength)
                {
                    return false;
                }

                using (var ecdsa = ECDsa.Create())
                {
                    ecdsa.ImportSubjectPublicKeyInfo(key.SubjectPublicKeyInfo, out _);
                    return ecdsa.VerifyData(
                        envelope.SigningInput,
                        envelope.Signature,
                        HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                }

            default:
                // Unreachable: TrustedKeyTable refuses an unknown algorithm at construction. Refusing
                // rather than throwing keeps a future table mistake from crashing the application.
                return false;
        }
    }
}
