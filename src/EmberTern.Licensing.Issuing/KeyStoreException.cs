using System;

namespace EmberTern.Licensing.Issuing;

/// <summary>
/// Why a keystore could not be opened.
///
/// <para>⭐ <b>The distinction that matters is <see cref="WrongPassphrase"/> versus
/// <see cref="Corrupt"/>, and it is the reason the keystore uses AES-<b>GCM</b>.</b> An authenticated
/// mode makes a wrong key fail as an <i>authentication</i> failure, so the operator can be told "wrong
/// passphrase" instead of "damaged file". Their next action is completely different — retype, versus
/// reach for the backup — and under an unauthenticated mode the two are indistinguishable. This is the
/// same reasoning <c>PassphraseProtector</c> records for the settings export, and it is deliberately the
/// same shape.</para>
/// </summary>
public enum KeyStoreFailure
{
    /// <summary>The file is not an EmberTern keystore at all.</summary>
    NotAKeyStore,

    /// <summary>Written by a newer build. ⛔ Refused rather than partially read.</summary>
    UnsupportedVersion,

    /// <summary>The encryption scheme or KDF named in the header is not one this build implements.</summary>
    UnsupportedScheme,

    /// <summary>The passphrase is wrong, or the encrypted payload was modified.</summary>
    WrongPassphrase,

    /// <summary>Structurally damaged — truncated, not base64, or decrypted into something unreadable.</summary>
    Corrupt,

    /// <summary>The keystore opened, but does not contain the requested key id.</summary>
    KeyNotFound,
}

/// <summary>A keystore operation that could not complete. <see cref="Failure"/> says why.</summary>
public sealed class KeyStoreException : Exception
{
    /// <summary>Creates the exception.</summary>
    public KeyStoreException(KeyStoreFailure failure, string message, Exception? innerException = null)
        : base(message, innerException) => Failure = failure;

    /// <summary>
    /// The classified reason. ⚠ The License Manager maps this to a sentence in its own catalog — ⛔ the
    /// <see cref="Exception.Message"/> here is English diagnostics for a log, not text to show an operator.
    /// </summary>
    public KeyStoreFailure Failure { get; }
}
