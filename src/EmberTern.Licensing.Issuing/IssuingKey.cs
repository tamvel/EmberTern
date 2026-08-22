using System;
using System.Security.Cryptography;

namespace EmberTern.Licensing.Issuing;

/// <summary>
/// A key unlocked from a <see cref="KeyStore"/> and ready to sign.
///
/// <para>⭐ <b><see cref="Sign"/> is <c>internal</c>.</b> Nothing outside this assembly can sign
/// arbitrary bytes — the only way to produce a signature is to ask <see cref="LicenseIssuer"/> for a
/// licence, which means every signature this system ever emits went through the validation and the
/// self-verification in one place. A public <c>Sign(byte[])</c> would be a signing oracle wearing a
/// helpful name.</para>
/// </summary>
public sealed class IssuingKey : IDisposable
{
    private readonly ECDsa _ecdsa;
    private bool _disposed;

    private IssuingKey(string keyId, SignatureAlgorithm algorithm, DateTimeOffset createdAt, ECDsa ecdsa)
    {
        KeyId = keyId;
        Algorithm = algorithm;
        CreatedAt = createdAt;
        _ecdsa = ecdsa;
    }

    /// <summary>The <c>kid</c> that will appear in every licence signed with this key.</summary>
    public string KeyId { get; }

    /// <summary>The algorithm.</summary>
    public SignatureAlgorithm Algorithm { get; }

    /// <summary>When the key was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    internal static IssuingKey FromEntry(KeyStoreEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new IssuingKey(entry.KeyId, entry.Algorithm, entry.CreatedAt, entry.CreateKey());
    }

    /// <summary>The public half, DER SubjectPublicKeyInfo — the half that ships.</summary>
    public byte[] ExportPublicKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _ecdsa.ExportSubjectPublicKeyInfo();
    }

    /// <summary>
    /// The trusted-key table this key's own output verifies against — used by
    /// <see cref="LicenseIssuer"/> to check its work before handing an artifact out.
    /// </summary>
    public TrustedKeyTable AsTrustedKeyTable() =>
        new([new TrustedKey(KeyId, Algorithm, ExportPublicKey())]);

    internal byte[] Sign(byte[] data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Algorithm switch
        {
            // ⛔ IEEE P1363 (fixed 64-byte r‖s), never DER — the verifier refuses DER by length, and a
            //    variable-length signature would drag an ASN.1 parser onto the verification path.
            SignatureAlgorithm.EcdsaP256Sha256 => _ecdsa.SignData(
                data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            _ => throw new NotSupportedException($"Unsupported signature algorithm '{Algorithm}'."),
        };
    }

    /// <summary>Releases the key.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _ecdsa.Dispose();
        _disposed = true;
    }
}
