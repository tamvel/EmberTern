using System;
using System.Security.Cryptography;
using System.Text;

namespace EmberTern.Core.Security;

/// <summary>
/// Builds the portable <see cref="EncryptionSchemes.PassphraseAes256"/> protector: <b>AES-256-GCM</b> under a
/// <b>PBKDF2-SHA256</b> key derived from a user passphrase. The scheme reserved by name for configuration
/// export/import (Settings Center etap 5a), and the answer to DPAPI's deliberate non-portability.
///
/// <para><b>⭐ GCM rather than CBC, and the reason is a behaviour rather than a preference.</b> An authenticated
/// mode makes a wrong passphrase fail as an <i>authentication</i> failure —
/// <see cref="AuthenticationTagMismatchException"/> — which the caller can report as <i>"wrong passphrase"</i>
/// and distinguish from a genuinely damaged file. Under CBC a wrong key yields garbage that then fails JSON
/// parsing, and the user is told "corrupt file" when the truth was "wrong passphrase". That is the same
/// distinction <c>SettingsLoadStatus</c> draws between <c>Corrupt</c> and <c>Unreadable</c>, for the same
/// reason: the two have different prognoses and the user's next action differs.</para>
///
/// <para><b>Why the KDF parameters are inputs and travel in cleartext.</b> A passphrase-derived key cannot be
/// reproduced without the salt and the iteration count, so they are stored in the export's header — standard
/// practice, and not secret. Storing them is also what lets a future build with a different default iteration
/// count still read an old file: the file states what it used.</para>
///
/// <para>⚠ <b>The passphrase is unrecoverable.</b> There is no reset and no back door — a forgotten passphrase
/// makes the file permanently unreadable. That is a consequence of the ratified design, not an argument against
/// it, but it has to be said where the user types the passphrase rather than discovered afterwards.</para>
///
/// <para>Crypto lives in Core here, unlike DPAPI: <c>AesGcm</c> and <c>Rfc2898DeriveBytes</c> are
/// cross-platform BCL primitives, whereas <c>ProtectedData</c> is Windows-only, which is the whole reason
/// <see cref="SecretProtector"/> takes the platform implementation as delegates. Architecture rule #1 is about
/// Avalonia types in Core, not about the framework's own cryptography.</para>
/// </summary>
public static class PassphraseProtector
{
    /// <summary>The one KDF identifier this build writes and accepts. Persisted verbatim in the export header,
    /// so it is append-only exactly as <see cref="EncryptionSchemes"/>' values are.</summary>
    public const string Pbkdf2Sha256 = "PBKDF2-SHA256";

    /// <summary>
    /// PBKDF2-SHA256 iterations for a newly written export.
    /// <para>⚠ Read from the file on import, never assumed — that is the point of storing it. Raising this
    /// constant must therefore not break existing files, and it does not.</para>
    /// </summary>
    public const int DefaultIterations = 600_000;

    /// <summary>Per-file random salt length. Never reused across files.</summary>
    public const int SaltBytes = 32;

    /// <summary>
    /// Accepted iteration bounds for a file we are asked to READ.
    /// <para>⚠ The upper bound is a denial-of-service guard, not fussiness: the iteration count is a number in
    /// a cleartext header that anyone can edit, and honouring a claimed two billion iterations would hang the
    /// app inside the KDF with no way out. A header outside these bounds is malformed, which is a clean
    /// refusal.</para>
    /// </summary>
    public const int MinIterations = 1;

    /// <inheritdoc cref="MinIterations"/>
    public const int MaxIterations = 10_000_000;

    // AES-256 key, and GCM's standard 96-bit nonce / 128-bit tag. The nonce is fresh per Protect call and
    // stored with the ciphertext; it is not secret, only unique.
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    /// <summary>True for a KDF identifier this build can honour. Anything else is refused <i>before</i> the
    /// user is asked for a passphrase — see <c>SettingsImportReader</c>'s ordered checks.</summary>
    public static bool IsSupportedKdf(string? kdf)
        => string.Equals(kdf, Pbkdf2Sha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for an iteration count inside <see cref="MinIterations"/>..<see cref="MaxIterations"/>.</summary>
    public static bool IsSupportedIterations(int iterations)
        => iterations >= MinIterations && iterations <= MaxIterations;

    /// <summary>A fresh cryptographically random salt, <see cref="SaltBytes"/> long.</summary>
    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltBytes);

    /// <summary>
    /// A protector that encrypts and decrypts with the key derived from <paramref name="passphrase"/>,
    /// <paramref name="salt"/> and <paramref name="iterations"/>.
    ///
    /// <para>The key is derived <b>once, here</b>, not per call: PBKDF2 at production iteration counts costs a
    /// noticeable fraction of a second by design, and an export writes one payload and an import reads one.</para>
    ///
    /// <para><b>What <c>Unprotect</c> throws, because callers classify on it:</b>
    /// <see cref="AuthenticationTagMismatchException"/> for a wrong passphrase or a tampered payload (GCM cannot
    /// tell those apart, and neither should we claim to), and <see cref="CryptographicException"/> for a payload
    /// that is not even shaped like one of ours. <see cref="SecretProtector"/> does not swallow either.</para>
    /// </summary>
    /// <exception cref="ArgumentException">The passphrase is empty, the salt is empty, the iteration count is
    /// outside the accepted bounds, or the KDF is one this build does not implement.</exception>
    public static SecretProtector Create(
        string passphrase, byte[] salt, int iterations, string kdf = Pbkdf2Sha256)
    {
        // An EMPTY PASSPHRASE IS NOT A VALID EXPORT, and refusing it here is what makes "every export is
        // encrypted" (ratified Q3) unrepresentable to violate rather than merely documented.
        if (string.IsNullOrEmpty(passphrase))
        {
            throw new ArgumentException("A passphrase is required — every export is encrypted.", nameof(passphrase));
        }
        if (salt is null || salt.Length == 0)
        {
            throw new ArgumentException("A salt is required.", nameof(salt));
        }
        if (!IsSupportedIterations(iterations))
        {
            throw new ArgumentException(
                $"Iterations must be between {MinIterations} and {MaxIterations}.", nameof(iterations));
        }
        if (!IsSupportedKdf(kdf))
        {
            throw new ArgumentException($"Unsupported key-derivation function '{kdf}'.", nameof(kdf));
        }

        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, KeyBytes);

        return new SecretProtector(
            EncryptionSchemes.PassphraseAes256,
            plaintext => Encrypt(key, plaintext),
            stored => Decrypt(key, stored));
    }

    // stored := Base64( nonce || tag || ciphertext ). Nonce and tag are not secret; keeping them in the blob
    // rather than in the header keeps the header a description of the FILE and the blob self-contained.
    private static string Encrypt(byte[] key, string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var body = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[body.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, body, cipher, tag);

        var blob = new byte[NonceBytes + TagBytes + cipher.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceBytes);
        cipher.CopyTo(blob, NonceBytes + TagBytes);
        return Convert.ToBase64String(blob);
    }

    private static string Decrypt(byte[] key, string stored)
    {
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(stored);
        }
        catch (FormatException ex)
        {
            // Not even Base64 — damage or the wrong kind of file entirely, and emphatically NOT an
            // authentication failure. Re-thrown as a CryptographicException so a caller classifying on the
            // exception type cannot mistake it for a wrong passphrase.
            throw new CryptographicException("The encrypted payload is not valid Base64.", ex);
        }

        if (blob.Length < NonceBytes + TagBytes)
        {
            throw new CryptographicException("The encrypted payload is truncated.");
        }

        var nonce = blob.AsSpan(0, NonceBytes);
        var tag = blob.AsSpan(NonceBytes, TagBytes);
        var cipher = blob.AsSpan(NonceBytes + TagBytes);
        var body = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagBytes);
        // Throws AuthenticationTagMismatchException on a wrong key or a modified payload. Deliberately NOT
        // caught here: the whole value of GCM in this design is that the caller gets to tell that apart from
        // damage, and swallowing it would throw that away.
        aes.Decrypt(nonce, cipher, tag, body);

        return Encoding.UTF8.GetString(body);
    }
}
