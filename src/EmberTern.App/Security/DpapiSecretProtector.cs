using System;
using System.Security.Cryptography;
using System.Text;
using EmberTern.Core.Security;

namespace EmberTern.App.Security;

// DPAPI-backed secret protection, Windows CurrentUser scope. Ciphertext is bound
// to the current Windows user account: a connections.json copied to another
// machine or user account cannot be decrypted — Unprotect throws there, and the
// store degrades those passwords to empty so the user simply re-enters them.
//
// This is the production implementation behind the Core SecretProtector seam. Any
// future store that holds secrets (ApplicationSettings, config export/import) wires
// the same protector via Create() rather than re-deriving crypto. DPAPI is the
// deliberate choice over a hand-rolled cipher: no key to store, the OS manages it.
public static class DpapiSecretProtector
{
    // App-specific entropy. NOT a secret (it ships in the binary) — it only
    // namespaces our DPAPI blobs so they aren't interchangeable with other apps'
    // CurrentUser blobs. Changing this string invalidates every previously stored
    // value (they fall back to empty on the next load).
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EmberTern.v1.secret");

    // Wraps Protect/Unprotect into the Core seam for injection into stores. Declares the
    // DPAPI scheme so the settings-file container header records how the payload was
    // encrypted (lets a load pick this protector and reject unknown future schemes).
    public static SecretProtector Create() => new(EncryptionSchemes.Dpapi, Protect, Unprotect);

    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(UiStrings.DpapiWindowsOnly);
        }

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return string.Empty;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(UiStrings.DpapiWindowsOnly);
        }

        var encrypted = Convert.FromBase64String(stored);
        var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
