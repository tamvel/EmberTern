using System;
using System.Text;

namespace EmberTern.Licensing.Issuing;

/// <summary>What a ceremony produced. ⚠ <see cref="KeyStoreFile"/> holds the encrypted private key.</summary>
/// <param name="KeyId">The key id.</param>
/// <param name="KeyStoreFile">The encrypted keystore, to be written to disk and backed up twice.</param>
/// <param name="PublicKey">The DER SubjectPublicKeyInfo — the only half that may leave the machine.</param>
/// <param name="TrustedKeyEntry">Ready-to-paste C# for <c>TrustedKeys.Production</c>.</param>
/// <param name="PublicKeyFingerprint">SHA-256 of the public key, for Appendix A and for comparing backups.</param>
public sealed record CeremonyResult(
    string KeyId, byte[] KeyStoreFile, byte[] PublicKey, string TrustedKeyEntry, string PublicKeyFingerprint);

/// <summary>The outcome of proving a backup actually works.</summary>
/// <param name="Succeeded">True only if every check below passed.</param>
/// <param name="Opened">The keystore decrypted with the passphrase.</param>
/// <param name="KeyPresent">It contains the expected key id.</param>
/// <param name="PublicKeyMatches">⭐ It is the SAME key, not merely a valid one.</param>
/// <param name="SignedAndVerified">A licence signed with it verified against the shipped public key.</param>
/// <param name="Detail">A diagnostic sentence for the ceremony log. ⛔ English, for a log, not for a user.</param>
public sealed record RestoreVerification(
    bool Succeeded, bool Opened, bool KeyPresent, bool PublicKeyMatches, bool SignedAndVerified, string Detail);

/// <summary>
/// The key ceremony — §24.1 of <c>docs/design/licensing-system.md</c>, as code rather than as a checklist.
///
/// <para>⭐ <b>Step 5 of that section is the one that matters and it is the one people skip:</b> <i>"verify
/// the restore — from the backup, on a different machine, sign a test licence and verify it. A backup that
/// has never been restored is a hypothesis."</i> <see cref="VerifyRestore"/> exists so that step is an
/// operation the License Manager can offer and a test can assert, instead of a bullet an operator reads
/// once and remembers differently.</para>
/// </summary>
public static class KeyCeremony
{
    /// <summary>
    /// Generates a signing key and seals it into a fresh keystore.
    ///
    /// <para>⛔ The returned <see cref="CeremonyResult.KeyStoreFile"/> is the ONLY copy. Losing it, or its
    /// passphrase, means nothing can ever be issued or renewed under this key again (§29). Back it up to
    /// two offline media in two locations, then run <see cref="VerifyRestore"/> against each.</para>
    /// </summary>
    public static CeremonyResult Perform(string keyId, string passphrase, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        var entry = KeyStoreEntry.Generate(keyId, createdAt);
        try
        {
            var publicKey = entry.ExportPublicKey();

            return new CeremonyResult(
                keyId,
                KeyStore.Create([entry], passphrase),
                publicKey,
                FormatTrustedKeyEntry(keyId, entry.Algorithm, publicKey),
                Fingerprint(publicKey));
        }
        finally
        {
            entry.Clear();
        }
    }

    /// <summary>
    /// Proves a keystore file is usable: it opens, holds the expected key, that key is the SAME one whose
    /// public half was shipped, and a licence signed with it verifies.
    ///
    /// <para>⭐ <paramref name="expectedPublicKey"/> is what makes this a real check. Without it the
    /// operation would only prove the backup contains <i>a</i> working key — and a backup of the wrong
    /// key passes that test while being exactly as useless as no backup at all.</para>
    ///
    /// <para>⛔ Never throws for a failure it is meant to detect: the caller wants a report, not an
    /// exception, because a failed restore verification is a finding to act on rather than a crash.</para>
    /// </summary>
    public static RestoreVerification VerifyRestore(
        byte[] keyStoreFile, string passphrase, string keyId, byte[] expectedPublicKey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(keyStoreFile);
        ArgumentNullException.ThrowIfNull(expectedPublicKey);

        KeyStore store;
        try
        {
            store = KeyStore.Open(keyStoreFile, passphrase);
        }
        catch (KeyStoreException e)
        {
            return new RestoreVerification(false, false, false, false, false,
                $"The keystore did not open: {e.Failure}.");
        }

        using (store)
        {
            IssuingKey key;
            try
            {
                key = store.Unlock(keyId);
            }
            catch (KeyStoreException e)
            {
                return new RestoreVerification(
                    Succeeded: false, Opened: true, KeyPresent: false, PublicKeyMatches: false,
                    SignedAndVerified: false,
                    $"The keystore opened but holds no key '{keyId}' ({e.Failure}).");
            }

            using (key)
            {
                var matches = CryptographicEquals(key.ExportPublicKey(), expectedPublicKey);
                if (!matches)
                {
                    return new RestoreVerification(
                        Succeeded: false, Opened: true, KeyPresent: true, PublicKeyMatches: false,
                        SignedAndVerified: false,
                        "The keystore opened and holds the key id, but it is a DIFFERENT key — " +
                        "this backup cannot renew the licences already in the field.");
                }

                var issuer = new LicenseIssuer(key);
                var probe = issuer.Issue(
                    new LicenseTerms
                    {
                        Licensee = "Ceremony probe",
                        Seats = 1,
                        NotBefore = now,
                        ExpiresAt = now.AddDays(1),
                    },
                    now);

                var verdict = LicenseVerifier.Verify(
                    probe.ArmoredText,
                    new LicenseVerificationContext(
                        new TrustedKeyTable([new TrustedKey(keyId, key.Algorithm, expectedPublicKey)]),
                        now,
                        LicenseConstants.ProductId,
                        LicenseConstants.MaxSupportedPayloadVersion,
                        LicenseConstants.DefaultGracePeriod,
                        BuildReleaseDate: null));

                var verified = verdict.Status == LicenseStatus.Valid;

                return new RestoreVerification(verified, true, true, true, verified,
                    verified
                        ? "The backup is usable: it opened, holds the expected key, and signed a licence " +
                          "that verifies against the shipped public key."
                        : $"The key signed, but the artifact did not verify ({verdict.Failure}).");
            }
        }
    }

    /// <summary>
    /// The exact C# to paste into <c>TrustedKeys.Production</c>.
    ///
    /// <para>⭐ Generated rather than transcribed, because transcription is where a ceremony goes wrong: a
    /// public key is 120-odd base64 characters that nobody proof-reads, and a single altered character
    /// produces a build that refuses every licence it will ever be shown.</para>
    /// </summary>
    public static string FormatTrustedKeyEntry(string keyId, SignatureAlgorithm algorithm, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        var base64 = Convert.ToBase64String(publicKey);
        var builder = new StringBuilder();

        builder.Append("new TrustedKey(\"").Append(keyId).Append("\", SignatureAlgorithm.")
            .Append(algorithm).Append(", Convert.FromBase64String(").AppendLine();

        for (var offset = 0; offset < base64.Length; offset += 72)
        {
            builder.Append("    \"").Append(base64, offset, Math.Min(72, base64.Length - offset));
            builder.AppendLine(offset + 72 >= base64.Length ? "\"))," : "\" +");
        }

        return builder.ToString();
    }

    /// <summary>SHA-256 of the public key, uppercase hex — for Appendix A and for comparing two backups.</summary>
    public static string Fingerprint(byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(publicKey));
    }

    private static bool CryptographicEquals(byte[] left, byte[] right) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
}
