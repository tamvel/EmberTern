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

    public SecretProtector(Func<string, string> protect, Func<string, string> unprotect)
    {
        _protect = protect ?? throw new ArgumentNullException(nameof(protect));
        _unprotect = unprotect ?? throw new ArgumentNullException(nameof(unprotect));
    }

    // No-op protector: stored value == plaintext. Used by tests and as the safe
    // default when no platform protector is injected. Production wires DPAPI via
    // EmberTern.App.Security.DpapiSecretProtector.
    public static SecretProtector Identity { get; } = new(static s => s, static s => s);

    // Plaintext -> stored (encrypted, typically Base64). Never called with a null;
    // callers pass string.Empty for "no secret".
    public string Protect(string plaintext) => _protect(plaintext);

    // Stored -> plaintext. May throw if the stored blob can't be decrypted (e.g. a
    // DPAPI value copied from another Windows account/machine); callers decide how
    // to degrade. SecretProtector itself does not swallow.
    public string Unprotect(string stored) => _unprotect(stored);
}
