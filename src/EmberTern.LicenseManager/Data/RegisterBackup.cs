using System;
using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EmberTern.LicenseManager.Data;

/// <summary>Why a backup could not be opened. Persisted nowhere, so this list may change freely.</summary>
public enum BackupFailure
{
    /// <summary>The file is not an EmberTern register backup at all.</summary>
    NotABackup,

    /// <summary>Written by a newer License Manager than this one.</summary>
    UnsupportedVersion,

    /// <summary>An encryption scheme or KDF this build does not implement.</summary>
    UnsupportedScheme,

    /// <summary>The container is damaged — truncated, malformed, or not valid base64.</summary>
    Corrupt,

    /// <summary>⭐ The passphrase is wrong, or the file was modified. See <see cref="RegisterBackup"/>.</summary>
    WrongPassphrase,
}

/// <summary>A backup could not be read.</summary>
public sealed class BackupException : Exception
{
    /// <summary>Creates the exception.</summary>
    public BackupException(BackupFailure failure, string message, Exception? inner = null)
        : base(message, inner) => Failure = failure;

    /// <summary>What went wrong.</summary>
    public BackupFailure Failure { get; }
}

/// <summary>What a backup says about itself before anything is decrypted.</summary>
/// <param name="Version">The container version.</param>
/// <param name="CreatedAt">When the backup was taken.</param>
/// <param name="SchemaVersion">The register schema the snapshot inside carries.</param>
/// <param name="Iterations">The KDF work factor the file was written with.</param>
public sealed record BackupHeader(
    int Version, DateTimeOffset CreatedAt, int SchemaVersion, int Iterations);

/// <summary>
/// The encrypted container around a register snapshot — <c>*.etlmbak</c>.
///
/// <para><b>AES-256-GCM under PBKDF2-SHA256</b>, with the same reviewed numbers as
/// <see cref="Licensing.Issuing.KeyStore"/>: 600 000 iterations, a 32-byte per-file salt, GCM's 96-bit
/// nonce and 128-bit tag. ⭐ The construction is shared; the SECRET is not. Decision D‑1 is that a backup
/// carries its own passphrase and its own mechanism — ⛔ nothing in this file, and nothing that calls it,
/// may reach for the keystore passphrase. An operator who chooses to type the same words is making an
/// operational choice, not using a shared key. <c>RegisterBackupTests</c> pins the parameters equal to the
/// keystore's so the two cannot drift apart silently.</para>
///
/// <para>⭐ <b>It works on bytes, never on a path</b>, exactly as the keystore does. Whose disk and what
/// the file is called belong to the caller, which is what makes every state here — wrong passphrase,
/// damaged file, future version — reachable in a test without a filesystem.</para>
///
/// <para>⚠ <b>One deliberate strengthening over the keystore's container: the cleartext header is bound
/// in as GCM associated data.</b> The keystore's header makes no claims about its contents, so binding it
/// would buy nothing. This one does — it states when the backup was taken and which schema is inside, and
/// the restore surface shows both to the operator BEFORE a passphrase is typed. Unauthenticated, that is a
/// sentence the file could lie about; as associated data, editing it makes the tag fail. ⛔ This is the
/// only difference from the keystore's construction, and it changes no algorithm and no parameter.</para>
///
/// <para>⛔ <b>The signing key is not in here.</b> A register backup carries <c>licenses.db</c> and
/// nothing else — §12.3 keeps "back up the register" and "back up the key" as two operations with two
/// risk profiles, and §24.2 forbids the private key from travelling in a backup at all.</para>
/// </summary>
public static class RegisterBackup
{
    /// <summary>Identifies the file before anything is decrypted.</summary>
    public const string Magic = "EMBERTERN-LM-BACKUP";

    /// <summary>The container version this build writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The one encryption scheme. Persisted verbatim, so append-only.</summary>
    public const string Scheme = "aes256-passphrase";

    /// <summary>The one KDF. Persisted verbatim, so append-only.</summary>
    public const string Kdf = "PBKDF2-SHA256";

    /// <summary>PBKDF2 iterations for a newly written backup. ⚠ Read from the file on open, never assumed.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>The extension the License Manager writes.</summary>
    public const string FileExtension = ".etlmbak";

    private const int SaltBytes = 32;
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int MinIterations = 1;
    private const int MaxIterations = 10_000_000;

    private const string TimestampFormat = Licensing.LicensePayload.TimestampFormat;

    /// <summary>Encrypts a register snapshot into a backup file's bytes.</summary>
    /// <param name="snapshot">The register snapshot — see <see cref="LicenseRegister.CreateSnapshot"/>.</param>
    /// <param name="passphrase">⭐ The backup's OWN passphrase (D‑1).</param>
    /// <param name="createdAt">When the backup was taken.</param>
    /// <param name="schemaVersion">The register schema the snapshot carries.</param>
    /// <param name="iterations">Overrides the KDF work factor. ⚠ For tests — production uses the default.</param>
    /// <exception cref="ArgumentException">An empty passphrase or an empty snapshot.</exception>
    public static byte[] Create(
        byte[] snapshot,
        string passphrase,
        DateTimeOffset createdAt,
        int schemaVersion,
        int? iterations = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // ⛔ An empty passphrase is not a backup. Refusing it here makes "a register backup is always
        //    encrypted at rest" unrepresentable to violate, rather than merely documented — the same
        //    move KeyStore.Create makes, for the same reason.
        if (string.IsNullOrEmpty(passphrase))
        {
            throw new ArgumentException(
                "A passphrase is required — a register backup is always encrypted.", nameof(passphrase));
        }

        if (snapshot.Length == 0)
        {
            // ⚠ An empty snapshot is not "a backup of an empty register" — an empty register is still a
            //    valid SQLite file with a schema in it. Zero bytes means the snapshot step produced
            //    nothing, and writing that out as a backup is how an operator ends up holding a file that
            //    restores to nothing.
            throw new ArgumentException("The snapshot is empty.", nameof(snapshot));
        }

        var effectiveIterations = iterations ?? DefaultIterations;
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = DeriveKey(passphrase, salt, effectiveIterations);

        try
        {
            var header = WriteHeader(salt, effectiveIterations, createdAt, schemaVersion);
            var blob = Encrypt(key, snapshot, header);
            return Compose(header, blob);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Reads what the file says about itself, without a passphrase and without decrypting.
    ///
    /// <para>⭐ This is what lets the restore surface refuse an unknown format, and show when a backup was
    /// taken, before asking the operator for anything. ⚠ Every field it returns is bound into the
    /// authentication tag, so a header edited after the fact fails <see cref="Open"/> — but this method
    /// itself cannot detect that, because detecting it requires the passphrase. Treat what it returns as a
    /// claim until an <see cref="Open"/> succeeds.</para>
    /// </summary>
    /// <exception cref="BackupException">See <see cref="BackupFailure"/>.</exception>
    public static BackupHeader ReadHeader(byte[] file)
    {
        var parsed = Parse(file);
        return new BackupHeader(
            CurrentVersion, parsed.CreatedAt, parsed.SchemaVersion, parsed.Iterations);
    }

    /// <summary>Decrypts a backup, returning the register snapshot inside it.</summary>
    /// <exception cref="BackupException">See <see cref="BackupFailure"/>.</exception>
    public static byte[] Open(byte[] file, string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            throw new BackupException(BackupFailure.WrongPassphrase, "A passphrase is required.");
        }

        var parsed = Parse(file);
        var key = DeriveKey(passphrase, parsed.Salt, parsed.Iterations);

        try
        {
            try
            {
                return Decrypt(key, parsed.Blob, parsed.Header);
            }
            catch (AuthenticationTagMismatchException e)
            {
                // ⭐ GCM's whole value here: the operator is told to retype rather than sent looking for
                //    another backup. ⛔ Do not merge this arm with the one below — "you mistyped" and
                //    "this file is damaged" are different situations with different next steps.
                //    ⚠ A tampered HEADER also lands here, because the header is this tag's associated
                //    data. That is the intended reading: a modified backup is not to be trusted, and the
                //    operator's next move is the same either way.
                throw new BackupException(
                    BackupFailure.WrongPassphrase,
                    "The passphrase is wrong, or the backup was modified.", e);
            }
            catch (CryptographicException e)
            {
                throw new BackupException(BackupFailure.Corrupt, "The backup payload is damaged.", e);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    // ── Container ───────────────────────────────────────────────────────────────────────────────────

    private static byte[] WriteHeader(
        byte[] salt, int iterations, DateTimeOffset createdAt, int schemaVersion)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            // ⭐ CLEARTEXT on purpose, and the property ORDER is part of the format: these exact bytes
            //    are the associated data, so they must be reproducible byte-for-byte on open. ⛔ Never
            //    reorder, reformat or re-indent this object — a cosmetic change here invalidates every
            //    backup ever written.
            writer.WriteStartObject();
            writer.WriteString("magic", Magic);
            writer.WriteNumber("version", CurrentVersion);
            writer.WriteString("scheme", Scheme);
            writer.WriteString("kdf", Kdf);
            writer.WriteNumber("iterations", iterations);
            writer.WriteString("salt", Convert.ToBase64String(salt));
            writer.WriteString("createdAt", Licensing.LicensePayload.FormatTimestamp(createdAt));
            writer.WriteNumber("schemaVersion", schemaVersion);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    // ⚠ The file is HEADER-BYTES + newline + base64(payload). The header is stored verbatim rather than
    //    re-serialised on open, because it is associated data: re-serialising would have to reproduce the
    //    writer's exact formatting, and the first time someone changed a whitespace option every existing
    //    backup would stop opening with no explanation.
    private static byte[] Compose(byte[] header, byte[] blob)
    {
        var payload = Encoding.ASCII.GetBytes(Convert.ToBase64String(blob));
        var file = new byte[header.Length + 1 + payload.Length];
        header.CopyTo(file, 0);
        file[header.Length] = (byte)'\n';
        payload.CopyTo(file, header.Length + 1);
        return file;
    }

    private static ParsedBackup Parse(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var split = Array.IndexOf(file, (byte)'\n');
        if (split <= 0 || split == file.Length - 1)
        {
            throw new BackupException(
                BackupFailure.NotABackup, "Not an EmberTern register backup.");
        }

        var header = file[..split];

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(header);
        }
        catch (JsonException e)
        {
            throw new BackupException(BackupFailure.NotABackup, "Not an EmberTern register backup.", e);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !string.Equals(Text(root, "magic"), Magic, StringComparison.Ordinal))
            {
                throw new BackupException(
                    BackupFailure.NotABackup, "Not an EmberTern register backup.");
            }

            if (!root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var versionNumber))
            {
                throw new BackupException(BackupFailure.Corrupt, "The backup header has no version.");
            }

            if (versionNumber > CurrentVersion)
            {
                throw new BackupException(
                    BackupFailure.UnsupportedVersion,
                    $"The backup is version {versionNumber}; this build reads {CurrentVersion}.");
            }

            if (!Text(root, "scheme").Equals(Scheme, StringComparison.Ordinal) ||
                !Text(root, "kdf").Equals(Kdf, StringComparison.Ordinal))
            {
                throw new BackupException(
                    BackupFailure.UnsupportedScheme, "The backup uses an unknown scheme or KDF.");
            }

            if (!root.TryGetProperty("iterations", out var iterationsElement) ||
                iterationsElement.ValueKind != JsonValueKind.Number ||
                !iterationsElement.TryGetInt32(out var iterations) ||
                iterations < MinIterations || iterations > MaxIterations)
            {
                // ⚠ The upper bound is a denial-of-service guard, not fussiness: the count sits in a
                //    cleartext header anyone can edit, and honouring a claimed two billion iterations
                //    would hang inside the KDF with no way out. Same reasoning as the keystore's.
                throw new BackupException(
                    BackupFailure.Corrupt, "The backup iteration count is missing or out of range.");
            }

            if (!root.TryGetProperty("schemaVersion", out var schemaElement) ||
                schemaElement.ValueKind != JsonValueKind.Number ||
                !schemaElement.TryGetInt32(out var schemaVersion))
            {
                throw new BackupException(BackupFailure.Corrupt, "The backup header has no schema version.");
            }

            if (!DateTimeOffset.TryParseExact(
                    Text(root, "createdAt"),
                    TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var createdAt))
            {
                throw new BackupException(BackupFailure.Corrupt, "The backup header has no valid timestamp.");
            }

            byte[] salt, blob;
            try
            {
                salt = Convert.FromBase64String(Text(root, "salt"));
                blob = Convert.FromBase64String(Encoding.ASCII.GetString(file, split + 1, file.Length - split - 1));
            }
            catch (FormatException e)
            {
                throw new BackupException(BackupFailure.Corrupt, "The backup is not valid base64.", e);
            }

            if (salt.Length == 0 || blob.Length < NonceBytes + TagBytes)
            {
                throw new BackupException(BackupFailure.Corrupt, "The backup is truncated.");
            }

            return new ParsedBackup(header, salt, iterations, createdAt, schemaVersion, blob);
        }
    }

    private sealed record ParsedBackup(
        byte[] Header, byte[] Salt, int Iterations, DateTimeOffset CreatedAt, int SchemaVersion, byte[] Blob);

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;

    // ── Crypto ──────────────────────────────────────────────────────────────────────────────────────

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, KeyBytes);

    private static byte[] Encrypt(byte[] key, byte[] plaintext, byte[] associatedData)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        using (var aes = new AesGcm(key, TagBytes))
        {
            aes.Encrypt(nonce, plaintext, cipher, tag, associatedData);
        }

        var blob = new byte[NonceBytes + TagBytes + cipher.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceBytes);
        cipher.CopyTo(blob, NonceBytes + TagBytes);
        return blob;
    }

    private static byte[] Decrypt(byte[] key, byte[] blob, byte[] associatedData)
    {
        var nonce = blob.AsSpan(0, NonceBytes);
        var tag = blob.AsSpan(NonceBytes, TagBytes);
        var cipher = blob.AsSpan(NonceBytes + TagBytes);
        var plaintext = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(nonce, cipher, tag, plaintext, associatedData);
        return plaintext;
    }
}
