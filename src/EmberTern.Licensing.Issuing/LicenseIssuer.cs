using System;
using System.Security.Cryptography;

namespace EmberTern.Licensing.Issuing;

/// <summary>The terms an administrator sets. Everything a licence asserts, and nothing else.</summary>
public sealed record LicenseTerms
{
    /// <summary>⭐ Required — the name the customer will see in About and Settings (decision D6).</summary>
    public required string Licensee { get; init; }

    /// <summary>Contractual seat count (decision D2). ⚠ Displayed by EmberTern, never enforced by it.</summary>
    public required int Seats { get; init; }

    /// <summary>Start of validity.</summary>
    public required DateTimeOffset NotBefore { get; init; }

    /// <summary>End of validity.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Perpetual-fallback boundary. ⚠ Nothing sets this in V1 (§3, §13.5).</summary>
    public DateTimeOffset? MaintenanceUntil { get; init; }

    /// <summary>Which product. Defaults to EmberTern.</summary>
    public string Product { get; init; } = LicenseConstants.ProductId;

    /// <summary>
    /// The stable licence identity. <see langword="null"/> generates a fresh one.
    /// ⭐ A RENEWAL passes the existing id: same <c>lid</c>, later <c>iat</c> — that pair is what makes
    /// EmberTern accept the new artifact as a replacement rather than as a different licence (§16.4).
    /// </summary>
    public string? LicenseId { get; init; }
}

/// <summary>A signed licence, in every form the License Manager needs.</summary>
/// <param name="Payload">What was asserted.</param>
/// <param name="PayloadJson">The exact bytes that were signed — stored verbatim in the register.</param>
/// <param name="Token">The bare <c>ETL1.…</c> token.</param>
/// <param name="ArmoredText">The content of <c>EmberTern.etlic</c>.</param>
public sealed record IssuedLicense(
    LicensePayload Payload, byte[] PayloadJson, string Token, string ArmoredText);

/// <summary>
/// Turns terms into a signed artifact. ⭐ <b>The only thing in this system that produces a signature.</b>
/// </summary>
public sealed class LicenseIssuer
{
    private readonly IssuingKey _key;

    /// <summary>Creates an issuer over an unlocked key.</summary>
    public LicenseIssuer(IssuingKey key) => _key = key ?? throw new ArgumentNullException(nameof(key));

    /// <summary>
    /// Signs a licence.
    ///
    /// <para>⭐⭐ <b>It verifies its own output before returning it.</b> The freshly built artifact is run
    /// through the real <see cref="LicenseVerifier"/> against this key's own public half, and anything
    /// short of "authenticates" throws. This is Architecture rule 11 at the source: an artifact we cannot
    /// prove is good must not leave the building, and the alternative to catching a key or format mistake
    /// here is catching it in a customer's inbox.</para>
    ///
    /// <para>⚠ The self-check asserts the artifact <i>authenticates</i>, not that it is currently
    /// <c>Valid</c> — a licence issued to start next month legitimately verifies as
    /// <see cref="LicenseStatus.NotYetValid"/>, and a self-check that demanded <c>Valid</c> would make
    /// post-dated licences unissuable.</para>
    /// </summary>
    /// <exception cref="ArgumentException">The terms are not issuable.</exception>
    /// <exception cref="CryptographicException">The artifact failed its own verification.</exception>
    public IssuedLicense Issue(LicenseTerms terms, DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(terms);
        Validate(terms);

        var payload = new LicensePayload
        {
            Version = LicenseConstants.MaxSupportedPayloadVersion,
            KeyId = _key.KeyId,
            AlgorithmId = SignatureAlgorithmIds.ToWireId(_key.Algorithm),
            LicenseId = terms.LicenseId ?? NewLicenseId(),
            Product = terms.Product,
            Licensee = terms.Licensee.Trim(),
            Seats = terms.Seats,
            IssuedAt = Truncate(issuedAt),
            NotBefore = Truncate(terms.NotBefore),
            ExpiresAt = Truncate(terms.ExpiresAt),
            MaintenanceUntil = terms.MaintenanceUntil is { } m ? Truncate(m) : null,
        };

        var payloadJson = payload.WriteJson();
        var segment = LicenseEnvelope.EncodePayload(payloadJson);
        var token = LicenseEnvelope.Compose(segment, _key.Sign(LicenseEnvelope.BuildSigningInput(segment)));

        SelfVerify(token, payload);

        return new IssuedLicense(payload, payloadJson, token, LicenseArmor.Wrap(token));
    }

    /// <summary>A fresh 128-bit licence id, lowercase hex.</summary>
    public static string NewLicenseId() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private void SelfVerify(string token, LicensePayload payload)
    {
        var verdict = LicenseVerifier.Verify(
            token,
            new LicenseVerificationContext(
                _key.AsTrustedKeyTable(),
                payload.NotBefore,
                payload.Product,
                LicenseConstants.MaxSupportedPayloadVersion,
                LicenseConstants.DefaultGracePeriod,
                BuildReleaseDate: null));

        if (verdict.Status == LicenseStatus.Invalid)
        {
            throw new CryptographicException(
                $"The issued licence failed its own verification ({verdict.Failure}). " +
                "Nothing was handed out. This is a key or format fault, not a bad input.");
        }
    }

    private static void Validate(LicenseTerms terms)
    {
        // ⚠ These are the LAST line, not the user-facing message. The License Manager validates in its UI,
        //    in the operator's language; reaching one of these throws is a programming error there.
        if (string.IsNullOrWhiteSpace(terms.Licensee))
        {
            throw new ArgumentException("A licensee name is required.", nameof(terms));
        }

        if (terms.Seats < 1)
        {
            throw new ArgumentException("A licence must carry at least one seat.", nameof(terms));
        }

        if (string.IsNullOrWhiteSpace(terms.Product))
        {
            throw new ArgumentException("A product is required.", nameof(terms));
        }

        if (terms.ExpiresAt <= terms.NotBefore)
        {
            throw new ArgumentException("The expiry must be after the start of validity.", nameof(terms));
        }

        if (terms.LicenseId is { Length: 0 })
        {
            throw new ArgumentException("A licence id, when given, must not be empty.", nameof(terms));
        }
    }

    // The wire format carries whole seconds (§13.2), so truncate here rather than letting a millisecond
    // vanish silently between what the register records and what the artifact says.
    private static DateTimeOffset Truncate(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(
            utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, TimeSpan.Zero);
    }
}
