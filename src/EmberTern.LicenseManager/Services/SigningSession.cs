using System;
using System.IO;
using EmberTern.Licensing;
using EmberTern.Licensing.Issuing;

namespace EmberTern.LicenseManager.Services;

/// <summary>
/// The unlocked signing key, for as long as the application is running.
///
/// <para>⛔ <b>The passphrase is never stored, never cached and never written anywhere</b> — it is used to
/// open the keystore and then goes out of scope. Locking the application means disposing this, and the
/// only way back in is to type it again.</para>
/// </summary>
public sealed class SigningSession : IDisposable
{
    private readonly KeyStore _store;

    private SigningSession(KeyStore store, IssuingKey key)
    {
        _store = store;
        Key = key;
        Issuer = new LicenseIssuer(key);
    }

    /// <summary>The unlocked key.</summary>
    public IssuingKey Key { get; }

    /// <summary>The one thing that can produce a signature.</summary>
    public LicenseIssuer Issuer { get; }

    /// <summary>The <c>kid</c> every licence issued in this session will carry.</summary>
    public string KeyId => Key.KeyId;

    /// <summary>
    /// Performs the ceremony and writes a brand-new keystore.
    ///
    /// <para>⛔ Refuses to overwrite an existing keystore. Overwriting one is not a mistake that can be
    /// undone: every licence in the field was signed by the key it held, and nothing can renew them
    /// afterwards (§29). The file must be moved aside by hand, deliberately.</para>
    /// </summary>
    public static SigningSession Create(ManagerPaths paths, string keyId, string passphrase, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.HasKeyStore)
        {
            throw new InvalidOperationException(
                "A keystore already exists. Creating a second signing key would leave every licence " +
                "already issued unrenewable.");
        }

        var ceremony = KeyCeremony.Perform(keyId, passphrase, now);

        paths.EnsureFolder();
        WriteAtomic(paths.KeyStore, ceremony.KeyStoreFile);

        return Unlock(paths, passphrase, keyId);
    }

    /// <summary>Opens an existing keystore.</summary>
    /// <exception cref="KeyStoreException">Wrong passphrase, damaged file, or no such key.</exception>
    public static SigningSession Unlock(ManagerPaths paths, string passphrase, string? keyId = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var store = KeyStore.Open(File.ReadAllBytes(paths.KeyStore), passphrase);
        try
        {
            var id = keyId ?? store.Entries[0].KeyId;
            return new SigningSession(store, store.Unlock(id));
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    /// <summary>The public half, for the ceremony record and for pasting into the client.</summary>
    public byte[] PublicKey => Key.ExportPublicKey();

    /// <summary>The trusted-key table this session's own output verifies against.</summary>
    public TrustedKeyTable TrustedKeys => Key.AsTrustedKeyTable();

    /// <summary>
    /// ⭐ Writes through a temporary file and replaces, so an interrupted write cannot leave a
    /// half-written keystore where the only copy of the signing key used to be.
    /// </summary>
    internal static void WriteAtomic(string path, byte[] content)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, content);

        if (File.Exists(path))
        {
            File.Replace(temporary, path, null);
        }
        else
        {
            File.Move(temporary, path);
        }
    }

    /// <summary>Locks up: clears the key material and forgets everything.</summary>
    public void Dispose()
    {
        Key.Dispose();
        _store.Dispose();
    }
}
