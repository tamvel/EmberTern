namespace EmberTern.Licensing;

/// <summary>
/// The signature algorithms this format can carry.
///
/// <para>⭐ <b>The algorithm is chosen by the trusted-key table entry, never by the artifact.</b> The
/// payload's <c>alg</c> field is cross-checked against the entry and refused on mismatch, but it never
/// selects anything — see <see cref="LicenseVerifier"/>. This is the JWT lesson: a token that names its
/// own algorithm can name a weak one, or none.</para>
///
/// <para>⛔ There is no <c>None</c> member and there never will be one. Values are append-only: a number
/// here is never reused, because a trusted-key table shipped in an old build persists this choice.</para>
/// </summary>
public enum SignatureAlgorithm
{
    /// <summary>
    /// ECDSA over NIST P‑256 with SHA‑256, signature as a fixed 64-byte <c>r‖s</c> concatenation
    /// (IEEE P1363). ⛔ Never DER: DER is variable-length and would drag an ASN.1 parser onto the
    /// verification path for no benefit. Ratified as decision D10.
    /// </summary>
    EcdsaP256Sha256 = 1,
}

/// <summary>
/// The wire spellings of <see cref="SignatureAlgorithm"/>, as they appear in a payload's <c>alg</c> field.
/// ⛔ Persisted verbatim inside signed artifacts, so these strings are APPEND-ONLY: never rename one, never
/// reuse one — the same discipline <c>EncryptionSchemes.cs</c> documents for settings schemes.
/// </summary>
public static class SignatureAlgorithmIds
{
    /// <summary>Wire spelling of <see cref="SignatureAlgorithm.EcdsaP256Sha256"/>.</summary>
    public const string EcdsaP256Sha256 = "ES256-P1363";

    /// <summary>The wire spelling registered for <paramref name="algorithm"/>.</summary>
    public static string ToWireId(SignatureAlgorithm algorithm) => algorithm switch
    {
        SignatureAlgorithm.EcdsaP256Sha256 => EcdsaP256Sha256,
        _ => throw new System.ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
    };
}
