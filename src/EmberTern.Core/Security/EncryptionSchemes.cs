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

    // AES-256-GCM under a passphrase-derived key (PBKDF2). Portable by design — this is the
    // scheme that makes a settings EXPORT readable on another machine and account, which
    // DPAPI deliberately is not. Built by PassphraseProtector.Create; the KDF parameters
    // (salt + iteration count) travel in the export's own cleartext header, because a key
    // derived from a passphrase cannot be reproduced without them.
    //
    // ⚠ IT IS NOT A settings.dat SCHEME, and this comment used to say the opposite. The
    // reserved note here read "add the protector and register it in
    // ApplicationSettingsStore.ResolveProtector, then start writing it as the scheme" —
    // written before the export had its own envelope. It does now (Settings Center etap 5a),
    // and that instruction does not survive the design:
    //
    //   · ResolveProtector answers "which protector decrypts THIS settings.dat payload", and
    //     it has no passphrase in scope. It could only ever return a protector that cannot
    //     decrypt, turning an honest refusal into a misleading "could not be decrypted".
    //   · An export dropped in place of settings.dat is correctly refused TODAY, precisely
    //     BECAUSE this scheme is unresolvable there: unknown scheme -> Future -> "written by a
    //     newer EmberTern build", file left intact. Registering it would remove that.
    //   · The protector an import needs is built per file, from that file's own header, by
    //     SettingsImportReader. Resolution is not the store's job here.
    //
    // So ResolveProtector carries an explicit arm that rejects this scheme with that reason
    // written on it, rather than a registration.
    public const string PassphraseAes256 = "aes256-passphrase";

    // ---- Reserved for future milestones (NOT implemented — do not emit yet) ---------
    // When one lands, add the protector and register it in
    // ApplicationSettingsStore.ResolveProtector, then start writing it as the scheme:
    //
    //   "aes256-machinekey" — at-rest AES if we ever move off DPAPI.
    //
    // ⚠ That instruction holds for an AT-REST scheme (one whose key the store can obtain on
    // its own). It does not generalise to a scheme that needs a credential from the user —
    // see PassphraseAes256 above for why.
}
