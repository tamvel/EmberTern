using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace EmberTern.Licensing;

/// <summary>
/// A public key this build accepts signatures from.
///
/// <para>⭐ <b>The entry dictates the algorithm.</b> A payload's <c>alg</c> is cross-checked against
/// <see cref="Algorithm"/> and refused on mismatch, but it never selects anything — see
/// <see cref="LicenseVerifier"/>.</para>
/// </summary>
/// <param name="KeyId">The <c>kid</c> this entry answers to.</param>
/// <param name="Algorithm">The algorithm signatures under this key must use.</param>
/// <param name="SubjectPublicKeyInfo">The public key in DER SPKI form.</param>
/// <param name="Revoked">
/// ⚠ Set by a later release when a key is compromised. It kills every licence ever signed with that key,
/// including honest ones, so a release that sets it may only ship AFTER every live licence has been
/// reissued under a new key — see §15.3.
/// </param>
public sealed record TrustedKey(
    string KeyId,
    SignatureAlgorithm Algorithm,
    byte[] SubjectPublicKeyInfo,
    bool Revoked = false);

/// <summary>
/// The set of keys a verification runs against. Passed in rather than reached for, so the verifier stays
/// a pure function of its inputs and every test can supply its own keys.
/// </summary>
public sealed class TrustedKeyTable
{
    private readonly Dictionary<string, TrustedKey> _byKeyId;

    /// <summary>
    /// ⭐ Validates every key at construction, so the verification path has no "our own key is broken"
    /// branch. A malformed entry is a programming error in <i>our</i> table, not a bad licence, and it
    /// must not be reported to a user as an invalid licence.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A duplicate <c>kid</c>, or a key that is not a usable public key of its declared algorithm.
    /// </exception>
    public TrustedKeyTable(IEnumerable<TrustedKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        _byKeyId = new Dictionary<string, TrustedKey>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key.KeyId))
            {
                throw new ArgumentException("A trusted key must have a key id.", nameof(keys));
            }

            if (!_byKeyId.TryAdd(key.KeyId, key))
            {
                throw new ArgumentException(
                    $"Duplicate trusted key id '{key.KeyId}'. Key ids are unique by construction.",
                    nameof(keys));
            }

            Validate(key, nameof(keys));
        }

        Keys = new ReadOnlyCollection<TrustedKey>([.. _byKeyId.Values]);
    }

    /// <summary>Every entry, in insertion order.</summary>
    public IReadOnlyList<TrustedKey> Keys { get; }

    /// <summary>An empty table. Every licence is refused with <see cref="LicenseFailure.UnknownKey"/>.</summary>
    public static TrustedKeyTable Empty { get; } = new([]);

    /// <summary>Looks a key up by its id. ⛔ There is no fallback and no "try them all".</summary>
    public bool TryGet(string keyId, [NotNullWhen(true)] out TrustedKey? key) =>
        _byKeyId.TryGetValue(keyId, out key);

    private static void Validate(TrustedKey key, string parameterName)
    {
        switch (key.Algorithm)
        {
            case SignatureAlgorithm.EcdsaP256Sha256:
                using (var ecdsa = ECDsa.Create())
                {
                    try
                    {
                        ecdsa.ImportSubjectPublicKeyInfo(key.SubjectPublicKeyInfo, out _);
                    }
                    catch (CryptographicException e)
                    {
                        throw new ArgumentException(
                            $"Trusted key '{key.KeyId}' is not a readable SubjectPublicKeyInfo.",
                            parameterName,
                            e);
                    }

                    if (ecdsa.KeySize != 256)
                    {
                        throw new ArgumentException(
                            $"Trusted key '{key.KeyId}' declares {SignatureAlgorithmIds.EcdsaP256Sha256} " +
                            $"but carries a {ecdsa.KeySize}-bit key.",
                            parameterName);
                    }
                }

                break;

            default:
                throw new ArgumentException(
                    $"Trusted key '{key.KeyId}' declares an unknown algorithm.", parameterName);
        }
    }
}

/// <summary>
/// The keys EmberTern ships with.
///
/// <para>⛔ <b>APPEND-ONLY.</b> An entry is never removed and never edited — only appended, or flagged
/// <see cref="TrustedKey.Revoked"/>. A key removed from this table is a population of licences that
/// stopped working, on machines we cannot reach. <c>EncryptionSchemes.cs</c> documents the same discipline
/// for settings schemes, for the same reason.</para>
///
/// <para>⭐ <b>It carries ONE key, <c>R1</c>, since the real ceremony of 2026-08-22 (L7.3/L7.4).</b> Before
/// that it was deliberately empty for five stages, so no production private key was carried through
/// development; every licence was refused with <see cref="LicenseFailure.UnknownKey"/>, which is the right
/// behaviour for a build that has no key to trust. ⚠ That emptiness used to be asserted by a test named
/// <c>…IsStillEmptyAtThisStage</c>; it was replaced — in the same change that added the key — by one that
/// asserts what the table now holds. A guard over an interim state has to be retired WITH the decision
/// that ends it, or on that day it reads exactly like a regression (gotcha #407).</para>
///
/// <para>⚠ A second key does not replace this one: it is <b>appended</b>, and signing moves to it only once
/// the release carrying it is widely deployed (§15.3). The stored fingerprint and the ceremony record live
/// in §35.4 of <c>docs/design/licensing-system.md</c>.</para>
/// </summary>
public static class TrustedKeys
{
    /// <summary>The production table. See the type remarks before touching it.</summary>
    public static TrustedKeyTable Production { get; } = new(
    [
        // ⭐ R1 — "root, first". Produced by the real key ceremony on 2026-08-22 (L7.3) and recorded in
        //    §35.4 of docs/design/licensing-system.md together with its fingerprint:
        //
        //        SHA-256  B55DCB8FAB7AD12EB77F798B89A59B5722AA11CAD71F27BE9DD49C7CFC0905AD
        //
        //    ⭐ GENERATED, never transcribed — this block is the verbatim output of
        //    KeyCeremony.FormatTrustedKeyEntry. A public key is 120-odd base64 characters nobody
        //    proof-reads, and one altered character produces a build that refuses every licence it will
        //    ever be shown. TheShippedTrustedKeyTableCarriesTheCeremonyKey recomputes the fingerprint from
        //    what is pasted here and compares it to the recorded value, so a transcription error fails the
        //    build rather than a customer.
        //
        // ⛔ Never paste a PRIVATE key here. PrivateKeyNeverShipsTests (L2) fails the build if anything
        //    under src/EmberTern.App or src/EmberTern.Licensing looks like one.
        new TrustedKey("R1", SignatureAlgorithm.EcdsaP256Sha256, Convert.FromBase64String(
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEsQPyDZ5zXbC2YlsDcxRjGptuMr4YdpTQemVK" +
            "4LspF917S0KkKAge1tBwvZNCQZCMpSSZqQ0EhFfxGbqX1ROoYw==")),
    ]);
}
