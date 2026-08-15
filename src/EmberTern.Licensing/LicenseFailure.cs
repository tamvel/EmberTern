namespace EmberTern.Licensing;

/// <summary>
/// Why a licence was refused. A <b>closed</b> set, on purpose.
///
/// <para>⚠ <b>Why this is not <c>MessageKey</c>, which is this project's ratified D‑3 currency.</b>
/// <c>MessageKey</c> exists so Core and Firebird can name a message without owning the words, and it
/// presumes ONE resource catalog — App's. ⭐ A licensing verdict is rendered by TWO applications with two
/// independent catalogs (EmberTern and the License Manager), so a key string would have to resolve in
/// both, and a key present in one and missing in the other fails silently — the exact shape of the
/// Phase‑5 localization defect. An enum is a closed set each application maps on its own terms, and the
/// compiler can see every value.</para>
///
/// <para>⛔ Never render one of these names to a user. Each application maps the value to a sentence in
/// its own catalog, in the user's language — Architecture rule 12 applies in full at the display site.</para>
///
/// <para>⚠ Only the <see cref="LicenseStatus.Invalid"/> status carries a value other than
/// <see cref="None"/>. <see cref="LicenseStatus.Expired"/>, <see cref="LicenseStatus.NotYetValid"/> and
/// <see cref="LicenseStatus.VersionNotCovered"/> describe a licence that <i>parsed and verified</i>; the
/// status alone says what is wrong, and duplicating it here would create two sources for one fact.</para>
/// </summary>
public enum LicenseFailure
{
    /// <summary>No failure. Every status except <see cref="LicenseStatus.Invalid"/> carries this.</summary>
    None = 0,

    /// <summary>
    /// No licence artifact was found. ⚠ Produced by the host application, never by
    /// <see cref="LicenseVerifier"/> — whether a file exists is a fact about a filesystem, which this
    /// assembly deliberately cannot see.
    /// </summary>
    FileMissing,

    /// <summary>The text is not an EmberTern licence at all — empty, or missing the <c>ETL1.</c> prefix.</summary>
    NotALicense,

    /// <summary>The <c>-----BEGIN/END EMBERTERN LICENSE-----</c> wrapper is unbalanced or repeated.</summary>
    MalformedArmor,

    /// <summary>The <c>ETL1.&lt;payload&gt;.&lt;signature&gt;</c> structure is wrong, or a segment is not base64url.</summary>
    MalformedEnvelope,

    /// <summary>The payload decoded, but is not the JSON object this format requires.</summary>
    MalformedPayload,

    /// <summary>
    /// The artifact declares a payload version (or an envelope generation) newer than this build
    /// understands. ⭐ The user-facing sentence for this one is "update EmberTern", not "invalid licence" —
    /// it is the only failure that is our age rather than their file.
    /// </summary>
    UnsupportedVersion,

    /// <summary>The <c>kid</c> names a key this build does not trust. ⛔ There is no fallback and no guessing.</summary>
    UnknownKey,

    /// <summary>The <c>kid</c> is known but has been revoked by a later release.</summary>
    RevokedKey,

    /// <summary>The payload's <c>alg</c> disagrees with the algorithm the trusted key is registered for.</summary>
    AlgorithmMismatch,

    /// <summary>The signature does not verify. The licence was modified, or it was not signed by us.</summary>
    SignatureInvalid,

    /// <summary>A valid, correctly signed licence — for a different product.</summary>
    WrongProduct,
}
