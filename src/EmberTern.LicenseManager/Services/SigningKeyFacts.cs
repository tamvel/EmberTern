using System;
using EmberTern.Licensing;
using EmberTern.Licensing.Issuing;

namespace EmberTern.LicenseManager.Services;

/// <summary>
/// The public half of the signing key, as the three facts a ceremony has to leave behind — plus the one
/// operation that turns a backup from a hypothesis into a verified copy.
///
/// <para>⭐⭐ <b>Why this type exists at all: <see cref="KeyCeremony"/> already produced every one of these
/// values, and <see cref="SigningSession.Create"/> threw them away.</b> It wrote the keystore and returned
/// an unlocked session, so the fingerprint, the public key and the ready-to-paste entry existed for the
/// duration of one method call and were never seen by anybody. §24.1 steps 5 and 7 — <i>verify the restore</i>
/// and <i>record the fingerprint</i> — therefore had no executor in the application, only in the test
/// suite.</para>
///
/// <para>⭐⭐ <b>And the reason it is a type rather than three properties on a view model:</b>
/// <see cref="KeyCeremony.VerifyRestore"/> takes the <i>expected</i> public key, which is what makes it a
/// real check — a backup of the WRONG key opens, holds the key id, and is exactly as useless as no backup
/// at all (§35.2). ⛔ <see cref="VerifyBackup"/> does not accept that argument: it supplies the running
/// session's own public half. There is no call site that can get it wrong, because there is no parameter
/// to get wrong.</para>
///
/// <para>⚠ Everything here is PUBLIC key material and may be shown, copied, logged and pasted into the
/// client. ⛔ Nothing on this type can reach the private half: it holds bytes, not a key, and it cannot
/// sign — <see cref="IssuingKey.Sign"/> is <c>internal</c> to the issuing assembly and reachable only
/// through <see cref="LicenseIssuer"/>.</para>
/// </summary>
public sealed class SigningKeyFacts
{
    private readonly byte[] _publicKey;

    /// <summary>Creates the facts over a public key.</summary>
    /// <param name="keyId">The <c>kid</c> every licence signed with this key carries.</param>
    /// <param name="algorithm">The algorithm the trusted-key entry will declare.</param>
    /// <param name="publicKey">The DER SubjectPublicKeyInfo — ⭐ the only half that may leave the machine.</param>
    public SigningKeyFacts(string keyId, SignatureAlgorithm algorithm, byte[] publicKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(publicKey);

        if (publicKey.Length == 0)
        {
            throw new ArgumentException("A public key cannot be empty.", nameof(publicKey));
        }

        KeyId = keyId;
        Algorithm = algorithm;

        // ⚠ Copied rather than aliased. The caller's array came from ExportPublicKey and is theirs; a
        //   fingerprint that could change under this object would be worse than no fingerprint.
        _publicKey = [.. publicKey];
    }

    /// <summary>Reads the facts off the unlocked session.</summary>
    /// <remarks>
    /// ⭐ The session is the ONE place a key is unlocked, so this is the only way these facts are obtained
    /// in the running application. ⛔ It takes the session and keeps nothing of it — no issuer, no key, no
    /// passphrase — so a surface holding these facts cannot sign.
    /// </remarks>
    public static SigningKeyFacts Of(SigningSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new SigningKeyFacts(session.KeyId, session.Key.Algorithm, session.PublicKey);
    }

    /// <summary>The <c>kid</c>. ⭐ <c>R1</c> — "root, first" — matches the ceremony register in §35.4.</summary>
    public string KeyId { get; }

    /// <summary>The algorithm signatures under this key must use.</summary>
    public SignatureAlgorithm Algorithm { get; }

    /// <summary>
    /// SHA-256 of the public key, uppercase hex — the value recorded in §35.4 and the value two machines
    /// compare to prove they are holding the same key.
    /// </summary>
    public string Fingerprint => KeyCeremony.Fingerprint(_publicKey);

    /// <summary>
    /// The public key itself, base64.
    ///
    /// <para>⚠ Standard base64, which is what <see cref="KeyCeremony.FormatTrustedKeyEntry"/> emits and
    /// what <c>Convert.FromBase64String</c> in the generated entry reads back. ⛔ Not base64url — §35.4's
    /// column heading says <i>base64url</i> and the code has always produced base64; the code is the
    /// contract, and the record follows it rather than the other way round.</para>
    /// </summary>
    public string PublicKeyBase64 => Convert.ToBase64String(_publicKey);

    /// <summary>
    /// The exact C# to paste into <c>TrustedKeys.Production</c>.
    ///
    /// <para>⭐ Generated, never transcribed: a public key is 120-odd base64 characters nobody proof-reads,
    /// and one altered character produces a build that refuses every licence it will ever be shown.</para>
    /// </summary>
    public string TrustedKeyEntry => KeyCeremony.FormatTrustedKeyEntry(KeyId, Algorithm, _publicKey);

    /// <summary>
    /// Proves a keystore file is a usable backup of THIS key: it opens, holds this key id, is the same key,
    /// and can sign a licence that verifies against the public half shown above.
    ///
    /// <para>⛔ Never throws for a failure it exists to detect — the caller wants a report, because a failed
    /// verification is a finding to act on rather than a crash.</para>
    /// </summary>
    /// <param name="keyStoreFile">The backup's bytes.</param>
    /// <param name="passphrase">⛔ A secret. It opens the backup and is not stored anywhere.</param>
    /// <param name="now">The moment the probe licence is issued for.</param>
    public RestoreVerification VerifyBackup(byte[] keyStoreFile, string passphrase, DateTimeOffset now) =>
        KeyCeremony.VerifyRestore(keyStoreFile, passphrase, KeyId, _publicKey, now);
}
