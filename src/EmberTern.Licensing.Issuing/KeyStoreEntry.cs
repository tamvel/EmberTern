using System;
using System.Security.Cryptography;

namespace EmberTern.Licensing.Issuing;

/// <summary>
/// One key inside a <see cref="KeyStore"/> — ⛔ <b>including its private half.</b>
///
/// <para>⭐ The private key is held as PKCS#8 bytes rather than as a live <see cref="ECDsa"/>, so it can
/// be wiped with <see cref="CryptographicOperations.ZeroMemory(Span{byte})"/> when the store closes. A
/// live key object cannot be scrubbed; a byte array can.</para>
///
/// <para>⚠ Wiping memory is hygiene, not a guarantee — the garbage collector may have moved the array,
/// and the operating system may have paged it. It raises the cost of a casual memory scrape; it does not
/// defeat an attacker who already owns the admin machine, and this document does not claim otherwise
/// (§25.1: a stolen admin laptop is a rotation event, not something the process can survive).</para>
/// </summary>
public sealed class KeyStoreEntry
{
    private byte[] _pkcs8;

    private KeyStoreEntry(
        string keyId, SignatureAlgorithm algorithm, DateTimeOffset createdAt, byte[] pkcs8, bool retired)
    {
        KeyId = keyId;
        Algorithm = algorithm;
        CreatedAt = createdAt;
        Retired = retired;
        _pkcs8 = pkcs8;
    }

    /// <summary>The <c>kid</c> licences signed with this key will carry.</summary>
    public string KeyId { get; }

    /// <summary>The algorithm this key signs with.</summary>
    public SignatureAlgorithm Algorithm { get; }

    /// <summary>When the ceremony produced it.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// ⚠ Retired means "stop ISSUING with this key". ⛔ It never means "stop verifying": every licence
    /// already signed with it must keep working, which is why the client's trusted-key table has no
    /// retirement concept at all — only revocation, which is a different and much more serious event
    /// (§15.3).
    /// </summary>
    public bool Retired { get; }

    /// <summary>Generates a brand-new key pair.</summary>
    public static KeyStoreEntry Generate(string keyId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new KeyStoreEntry(
            keyId, SignatureAlgorithm.EcdsaP256Sha256, createdAt, ecdsa.ExportPkcs8PrivateKey(), false);
    }

    /// <summary>Rebuilds an entry from stored material.</summary>
    internal static KeyStoreEntry FromPrivateKey(
        string keyId, SignatureAlgorithm algorithm, DateTimeOffset createdAt, byte[] pkcs8, bool retired) =>
        new(keyId, algorithm, createdAt, pkcs8, retired);

    /// <summary>
    /// The public half, in DER SubjectPublicKeyInfo form — ⭐ the only thing that may leave this machine.
    /// This is what gets pasted into <c>TrustedKeys.Production</c> and shipped to every customer.
    /// </summary>
    public byte[] ExportPublicKey()
    {
        using var ecdsa = CreateKey();
        return ecdsa.ExportSubjectPublicKeyInfo();
    }

    /// <summary>
    /// ⛔ <b>The private half.</b> <c>internal</c> so that only <see cref="KeyStore"/> can serialise it —
    /// nothing outside this assembly can obtain private key material through a public API, which is what
    /// makes "the key never leaves the keystore" a property of the type system rather than of discipline.
    /// </summary>
    internal byte[] ExportPrivateKey() =>
        _pkcs8.Length > 0 ? _pkcs8 : throw new ObjectDisposedException(nameof(KeyStoreEntry));

    internal ECDsa CreateKey()
    {
        ObjectDisposedException.ThrowIf(_pkcs8.Length == 0, this);

        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportPkcs8PrivateKey(_pkcs8, out _);
            return ecdsa;
        }
        catch
        {
            ecdsa.Dispose();
            throw;
        }
    }

    internal void Clear()
    {
        CryptographicOperations.ZeroMemory(_pkcs8);
        _pkcs8 = [];
    }
}
