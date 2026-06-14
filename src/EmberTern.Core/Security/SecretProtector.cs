using System;

namespace EmberTern.Core.Security;

// Reusable secret-protection seam. Core stays dependency-free (no Windows crypto
// here): the actual protect/unprotect implementation is injected as a delegate
// pair from the platform layer (App wires DPAPI). Stores that hold secrets accept
// a SecretProtector and run sensitive values through it at the JSON I/O boundary —
// plaintext in memory, ciphertext at rest.
//
// This is the foundation the upcoming ApplicationSettings store and the planned
// configuration export/import will share: each one takes the same SecretProtector
// rather than re-deriving crypto. It is a concrete class (not an interface) so it
// honours the "no interfaces without two implementations" rule; the two behaviours
// (DPAPI vs. Identity) are supplied as delegates, not subclasses.
public sealed class SecretProtector
{
    private readonly Func<string, string> _protect;
    private readonly Func<string, string> _unprotect;

    // Convenience overload: a protector with no declared scheme reports None (plaintext).
    // Kept so existing call sites and test fakes compile unchanged.
    public SecretProtector(Func<string, string> protect, Func<string, string> unprotect)
        : this(EncryptionSchemes.None, protect, unprotect)
    {
    }

    public SecretProtector(string scheme, Func<string, string> protect, Func<string, string> unprotect)
    {
        Scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
        _protect = protect ?? throw new ArgumentNullException(nameof(protect));
        _unprotect = unprotect ?? throw new ArgumentNullException(nameof(unprotect));
    }

    // Stable identifier (one of EncryptionSchemes.*) for the algorithm this protector
    // applies. Written into the settings.dat container header so a load can pick the
    // matching protector before decrypting. The Identity/no-op protector reports None.
    public string Scheme { get; }

    // No-op protector: stored value == plaintext. Used by tests and as the safe
    // default when no platform protector is injected. Production wires DPAPI via
    // EmberTern.App.Security.DpapiSecretProtector.
    public static SecretProtector Identity { get; } = new(EncryptionSchemes.None, static s => s, static s => s);

    // Plaintext -> stored (encrypted, typically Base64). Never called with a null;
    // callers pass string.Empty for "no secret".
    public string Protect(string plaintext) => _protect(plaintext);

    // Stored -> plaintext. May throw if the stored blob can't be decrypted (e.g. a
    // DPAPI value copied from another Windows account/machine); callers decide how
    // to degrade. SecretProtector itself does not swallow.
    public string Unprotect(string stored) => _unprotect(stored);
}
