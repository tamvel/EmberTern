namespace EmberTern.Core.Security;

// Stable string identifiers for the encryption scheme that produced a settings.dat
// payload. The active scheme is written into the (UNENCRYPTED) settings-file container
// header so a load can pick the right SecretProtector BEFORE attempting to decrypt — and
// reject a scheme it doesn't recognise (e.g. a file written by a newer build with a new
// algorithm) instead of silently failing to decrypt and losing everything.
//
// These identifiers are persisted verbatim, so they are APPEND-ONLY: never rename or
// reuse a value once shipped. Add new schemes here as they are introduced, and register a
// matching protector in ApplicationSettingsStore.ResolveProtector.
public static class EncryptionSchemes
{
    // Plaintext passthrough (SecretProtector.Identity). Tests and any unencrypted/dev
    // path. The on-disk payload is the raw JSON.
    public const string None = "none";

    // Windows DPAPI, CurrentUser scope (production at-rest). Not portable across
    // machines/accounts by design — see DpapiSecretProtector.
    public const string Dpapi = "dpapi";

    // ---- Reserved for future milestones (NOT implemented — do not emit yet) ---------
    // When one of these lands, add the protector and register it in
    // ApplicationSettingsStore.ResolveProtector, then start writing it as the scheme:
    //
    //   "aes256-passphrase" — portable, passphrase-derived key (config export/import).
    //   "aes256-machinekey" — at-rest AES if we ever move off DPAPI.
}
