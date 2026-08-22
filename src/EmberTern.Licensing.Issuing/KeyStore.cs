using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EmberTern.Licensing.Issuing;

/// <summary>
/// The encrypted store that holds the signing key — <c>keystore.etkeys</c>.
///
/// <para><b>AES-256-GCM under PBKDF2-SHA256.</b> The parameters deliberately match
/// <c>EmberTern.Core.Security.PassphraseProtector</c> — 600 000 iterations, a 32-byte per-file salt, GCM's
/// 96-bit nonce and 128-bit tag — so that the project has ONE reviewed set of numbers rather than two that
/// drift. ⚠ The code is not shared: this assembly must not reference Core, for the same reason
/// <c>EmberTern.Licensing</c> does not (§9.1). The pattern is shared; the dependency is not.</para>
///
/// <para>⭐ <b>It works on bytes, never on a path.</b> Whose disk, which folder and what the file is called
/// are the License Manager's business. Keeping I/O out means every state here — wrong passphrase, damaged
/// file, future version — is reachable in a test without a filesystem.</para>
///
/// <para>⚠ <b>The passphrase is unrecoverable.</b> There is no reset and no back door. That is a
/// consequence of the design rather than an argument against it, but it belongs where the operator types
/// it, not in a document they will read once.</para>
/// </summary>
public sealed class KeyStore : IDisposable
{
    /// <summary>Identifies the file before anything is decrypted.</summary>
    public const string Magic = "EMBERTERN-KEYSTORE";

    /// <summary>The container version this build writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The one encryption scheme. Persisted verbatim, so append-only.</summary>
    public const string Scheme = "aes256-passphrase";

    /// <summary>The one KDF. Persisted verbatim, so append-only.</summary>
    public const string Kdf = "PBKDF2-SHA256";

    /// <summary>PBKDF2 iterations for a newly written keystore. ⚠ Read from the file on open, never assumed.</summary>
    public const int DefaultIterations = 600_000;

    private const int SaltBytes = 32;
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int MinIterations = 1;
    private const int MaxIterations = 10_000_000;

    private readonly List<KeyStoreEntry> _entries;
    private bool _disposed;

    private KeyStore(List<KeyStoreEntry> entries)
    {
        _entries = entries;
        Entries = new ReadOnlyCollection<KeyStoreEntry>(entries);
    }

    /// <summary>What the store holds. ⚠ Each entry carries private key material in memory.</summary>
    public IReadOnlyList<KeyStoreEntry> Entries { get; }

    /// <summary>Encrypts a fresh keystore around <paramref name="entries"/>.</summary>
    /// <exception cref="ArgumentException">Empty passphrase, no entries, or duplicate key ids.</exception>
    public static byte[] Create(IEnumerable<KeyStoreEntry> entries, string passphrase, int? iterations = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // ⛔ An empty passphrase is not a keystore. Refusing it here makes "the signing key is always
        //    encrypted at rest" unrepresentable to violate, rather than merely documented.
        if (string.IsNullOrEmpty(passphrase))
        {
            throw new ArgumentException("A passphrase is required — a keystore is always encrypted.",
                nameof(passphrase));
        }

        var list = entries.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("A keystore must hold at least one key.", nameof(entries));
        }

        if (list.Select(e => e.KeyId).Distinct(StringComparer.Ordinal).Count() != list.Count)
        {
            throw new ArgumentException("Key ids must be unique within a keystore.", nameof(entries));
        }

        var effectiveIterations = iterations ?? DefaultIterations;
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = DeriveKey(passphrase, salt, effectiveIterations);

        try
        {
            var plaintext = SerializeEntries(list);
            try
            {
                var blob = Encrypt(key, plaintext);
                return WriteContainer(salt, effectiveIterations, blob);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Opens an encrypted keystore.</summary>
    /// <exception cref="KeyStoreException">See <see cref="KeyStoreFailure"/>.</exception>
    public static KeyStore Open(byte[] file, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (string.IsNullOrEmpty(passphrase))
        {
            throw new KeyStoreException(KeyStoreFailure.WrongPassphrase, "A passphrase is required.");
        }

        var (salt, iterations, blob) = ReadContainer(file);
        var key = DeriveKey(passphrase, salt, iterations);

        try
        {
            byte[] plaintext;
            try
            {
                plaintext = Decrypt(key, blob);
            }
            catch (AuthenticationTagMismatchException e)
            {
                // ⭐ GCM's whole value in this design: the operator is told to retype rather than sent to
                //    the backup. ⛔ Do not merge this arm with the one below.
                throw new KeyStoreException(
                    KeyStoreFailure.WrongPassphrase,
                    "The passphrase is wrong, or the keystore payload was modified.", e);
            }
            catch (CryptographicException e)
            {
                throw new KeyStoreException(KeyStoreFailure.Corrupt, "The keystore payload is damaged.", e);
            }

            try
            {
                return new KeyStore(DeserializeEntries(plaintext));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Re-encrypts the same keys under a new passphrase.</summary>
    public byte[] Save(string passphrase, int? iterations = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Create(_entries, passphrase, iterations);
    }

    /// <summary>Produces a usable signing key from the entry named <paramref name="keyId"/>.</summary>
    /// <exception cref="KeyStoreException"><see cref="KeyStoreFailure.KeyNotFound"/>.</exception>
    public IssuingKey Unlock(string keyId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entry = _entries.FirstOrDefault(e => string.Equals(e.KeyId, keyId, StringComparison.Ordinal))
            ?? throw new KeyStoreException(
                KeyStoreFailure.KeyNotFound, $"The keystore holds no key '{keyId}'.");

        return IssuingKey.FromEntry(entry);
    }

    /// <summary>Clears the private key material this store is holding in memory.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var entry in _entries)
        {
            entry.Clear();
        }

        _disposed = true;
    }

    // ── Container ───────────────────────────────────────────────────────────────────────────────────

    private static byte[] WriteContainer(byte[] salt, int iterations, byte[] blob)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(blob.Length + 512);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            // ⭐ The header is CLEARTEXT on purpose: an open must be able to pick the right scheme, and to
            //    refuse a scheme it does not know, BEFORE attempting to decrypt — otherwise an unreadable
            //    future file is indistinguishable from a wrong passphrase. Same reasoning as
            //    EncryptionSchemes.cs records for settings.dat.
            writer.WriteStartObject();
            writer.WriteString("magic", Magic);
            writer.WriteNumber("version", CurrentVersion);
            writer.WriteString("scheme", Scheme);
            writer.WriteString("kdf", Kdf);
            writer.WriteNumber("iterations", iterations);
            writer.WriteString("salt", Convert.ToBase64String(salt));
            writer.WriteString("payload", Convert.ToBase64String(blob));
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static (byte[] Salt, int Iterations, byte[] Blob) ReadContainer(byte[] file)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(file);
        }
        catch (JsonException e)
        {
            throw new KeyStoreException(KeyStoreFailure.NotAKeyStore, "Not an EmberTern keystore.", e);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("magic", out var magic) ||
                magic.ValueKind != JsonValueKind.String ||
                !string.Equals(magic.GetString(), Magic, StringComparison.Ordinal))
            {
                throw new KeyStoreException(KeyStoreFailure.NotAKeyStore, "Not an EmberTern keystore.");
            }

            if (!root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var versionNumber))
            {
                throw new KeyStoreException(KeyStoreFailure.Corrupt, "The keystore header has no version.");
            }

            if (versionNumber > CurrentVersion)
            {
                throw new KeyStoreException(
                    KeyStoreFailure.UnsupportedVersion,
                    $"The keystore is version {versionNumber}; this build reads {CurrentVersion}.");
            }

            if (!Text(root, "scheme").Equals(Scheme, StringComparison.Ordinal) ||
                !Text(root, "kdf").Equals(Kdf, StringComparison.Ordinal))
            {
                throw new KeyStoreException(
                    KeyStoreFailure.UnsupportedScheme, "The keystore uses an unknown scheme or KDF.");
            }

            if (!root.TryGetProperty("iterations", out var iterationsElement) ||
                iterationsElement.ValueKind != JsonValueKind.Number ||
                !iterationsElement.TryGetInt32(out var iterations) ||
                iterations < MinIterations || iterations > MaxIterations)
            {
                // ⚠ The upper bound is a denial-of-service guard, not fussiness: the count sits in a
                //    cleartext header anyone can edit, and honouring a claimed two billion iterations
                //    would hang inside the KDF with no way out.
                throw new KeyStoreException(
                    KeyStoreFailure.Corrupt, "The keystore iteration count is missing or out of range.");
            }

            byte[] salt, blob;
            try
            {
                salt = Convert.FromBase64String(Text(root, "salt"));
                blob = Convert.FromBase64String(Text(root, "payload"));
            }
            catch (FormatException e)
            {
                throw new KeyStoreException(KeyStoreFailure.Corrupt, "The keystore is not valid base64.", e);
            }

            if (salt.Length == 0 || blob.Length < NonceBytes + TagBytes)
            {
                throw new KeyStoreException(KeyStoreFailure.Corrupt, "The keystore is truncated.");
            }

            return (salt, iterations, blob);
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;

    // ── Entries ─────────────────────────────────────────────────────────────────────────────────────

    private static byte[] SerializeEntries(List<KeyStoreEntry> entries)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("keys");

            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("kid", entry.KeyId);
                writer.WriteString("alg", SignatureAlgorithmIds.ToWireId(entry.Algorithm));
                writer.WriteString("createdAt", LicensePayload.FormatTimestamp(entry.CreatedAt));
                writer.WriteString("pkcs8", Convert.ToBase64String(entry.ExportPrivateKey()));
                writer.WriteBoolean("retired", entry.Retired);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static List<KeyStoreEntry> DeserializeEntries(byte[] plaintext)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(plaintext);
        }
        catch (JsonException e)
        {
            throw new KeyStoreException(
                KeyStoreFailure.Corrupt, "The keystore decrypted into something unreadable.", e);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("keys", out var keys) ||
                keys.ValueKind != JsonValueKind.Array)
            {
                throw new KeyStoreException(KeyStoreFailure.Corrupt, "The keystore holds no key list.");
            }

            var entries = new List<KeyStoreEntry>();
            foreach (var element in keys.EnumerateArray())
            {
                var keyId = Text(element, "kid");
                var algorithmId = Text(element, "alg");
                var createdAtText = Text(element, "createdAt");
                var pkcs8Text = Text(element, "pkcs8");

                if (keyId.Length == 0 || pkcs8Text.Length == 0 ||
                    !string.Equals(algorithmId, SignatureAlgorithmIds.EcdsaP256Sha256, StringComparison.Ordinal) ||
                    !DateTimeOffset.TryParseExact(
                        createdAtText,
                        LicensePayload.TimestampFormat,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var createdAt))
                {
                    throw new KeyStoreException(KeyStoreFailure.Corrupt, "A keystore entry is malformed.");
                }

                byte[] pkcs8;
                try
                {
                    pkcs8 = Convert.FromBase64String(pkcs8Text);
                }
                catch (FormatException e)
                {
                    throw new KeyStoreException(KeyStoreFailure.Corrupt, "A keystore key is not base64.", e);
                }

                var retired = element.TryGetProperty("retired", out var retiredElement) &&
                              retiredElement.ValueKind == JsonValueKind.True;

                entries.Add(KeyStoreEntry.FromPrivateKey(
                    keyId, SignatureAlgorithm.EcdsaP256Sha256, createdAt, pkcs8, retired));
            }

            if (entries.Count == 0)
            {
                throw new KeyStoreException(KeyStoreFailure.Corrupt, "The keystore holds no keys.");
            }

            return entries;
        }
    }

    // ── Crypto ──────────────────────────────────────────────────────────────────────────────────────

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, KeyBytes);

    private static byte[] Encrypt(byte[] key, byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        using (var aes = new AesGcm(key, TagBytes))
        {
            aes.Encrypt(nonce, plaintext, cipher, tag);
        }

        var blob = new byte[NonceBytes + TagBytes + cipher.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceBytes);
        cipher.CopyTo(blob, NonceBytes + TagBytes);
        return blob;
    }

    private static byte[] Decrypt(byte[] key, byte[] blob)
    {
        var nonce = blob.AsSpan(0, NonceBytes);
        var tag = blob.AsSpan(NonceBytes, TagBytes);
        var cipher = blob.AsSpan(NonceBytes + TagBytes);
        var plaintext = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }
}
