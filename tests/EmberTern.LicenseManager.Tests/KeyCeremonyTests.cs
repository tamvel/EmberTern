using System;
using EmberTern.Licensing;
using EmberTern.Licensing.Issuing;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The key ceremony, and above all its step 5.
///
/// <para>⭐ <b>"Verify the restore" is the step people skip, and it is the only one whose omission is
/// unrecoverable.</b> Every other mistake in a ceremony is caught the first time you try to issue a
/// licence. A bad backup is caught the day the disk dies — which is the day the backup was supposed to
/// help. So the operation is code, and these tests are what say it works.</para>
/// </summary>
public sealed class KeyCeremonyTests
{
    private const string Passphrase = "six diceware words generated never remembered";
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ACeremonyProducesEverythingTheProcedureAsksFor()
    {
        var result = KeyCeremony.Perform("R1", Passphrase, Now);

        Assert.Equal("R1", result.KeyId);
        Assert.NotEmpty(result.KeyStoreFile);
        Assert.NotEmpty(result.PublicKey);
        Assert.Contains("new TrustedKey(\"R1\"", result.TrustedKeyEntry, StringComparison.Ordinal);
        Assert.Equal(64, result.PublicKeyFingerprint.Length);   // SHA-256, hex
    }

    [Fact]
    public void TheGeneratedKeyIsImmediatelyUsableByTheClientAssembly()
    {
        // The whole chain in one test: ceremony → keystore → unlock → issue → verify with the shipped
        // public key, exactly as EmberTern will.
        var result = KeyCeremony.Perform("R1", Passphrase, Now);

        using var store = KeyStore.Open(result.KeyStoreFile, Passphrase);
        using var key = store.Unlock("R1");

        var issued = new LicenseIssuer(key).Issue(
            new LicenseTerms { Licensee = "ACME", Seats = 1, NotBefore = Now, ExpiresAt = Now.AddYears(1) },
            Now);

        var verdict = LicenseVerifier.Verify(
            issued.ArmoredText,
            new LicenseVerificationContext(
                new TrustedKeyTable([new TrustedKey("R1", SignatureAlgorithm.EcdsaP256Sha256, result.PublicKey)]),
                Now, LicenseConstants.ProductId, LicenseConstants.MaxSupportedPayloadVersion,
                LicenseConstants.DefaultGracePeriod, null));

        Assert.Equal(LicenseStatus.Valid, verdict.Status);
    }

    [Fact]
    public void AGoodBackupVerifies()
    {
        var result = KeyCeremony.Perform("R1", Passphrase, Now);

        var report = KeyCeremony.VerifyRestore(result.KeyStoreFile, Passphrase, "R1", result.PublicKey, Now);

        Assert.True(report.Succeeded, report.Detail);
        Assert.True(report.Opened);
        Assert.True(report.KeyPresent);
        Assert.True(report.PublicKeyMatches);
        Assert.True(report.SignedAndVerified);
    }

    [Fact]
    public void ABackupOfTheWrongKeyIsDetected()
    {
        // ⭐⭐ THE test this operation exists for. Without comparing against the public key we actually
        //     shipped, the check would only prove the backup holds A working key — and a backup of the
        //     wrong key passes that while being exactly as useless as no backup at all.
        var shipped = KeyCeremony.Perform("R1", Passphrase, Now);
        var different = KeyCeremony.Perform("R1", Passphrase, Now);

        var report = KeyCeremony.VerifyRestore(
            different.KeyStoreFile, Passphrase, "R1", shipped.PublicKey, Now);

        Assert.False(report.Succeeded);
        Assert.True(report.Opened);
        Assert.True(report.KeyPresent);
        Assert.False(report.PublicKeyMatches);
        Assert.Contains("DIFFERENT key", report.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AWrongPassphraseIsReportedRatherThanThrown()
    {
        // ⛔ A failed restore verification is a finding to act on, not a crash: the operator is standing
        //    in front of a backup, and they need a report they can read and record.
        var result = KeyCeremony.Perform("R1", Passphrase, Now);

        var report = KeyCeremony.VerifyRestore(result.KeyStoreFile, "wrong", "R1", result.PublicKey, Now);

        Assert.False(report.Succeeded);
        Assert.False(report.Opened);
        Assert.Contains("WrongPassphrase", report.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingKeyIdIsReported()
    {
        var result = KeyCeremony.Perform("R1", Passphrase, Now);

        var report = KeyCeremony.VerifyRestore(result.KeyStoreFile, Passphrase, "R2", result.PublicKey, Now);

        Assert.False(report.Succeeded);
        Assert.True(report.Opened);
        Assert.False(report.KeyPresent);
    }

    [Fact]
    public void ADamagedBackupIsReported()
    {
        var result = KeyCeremony.Perform("R1", Passphrase, Now);
        var damaged = "this used to be a keystore"u8.ToArray();

        var report = KeyCeremony.VerifyRestore(damaged, Passphrase, "R1", result.PublicKey, Now);

        Assert.False(report.Succeeded);
        Assert.False(report.Opened);
    }

    [Fact]
    public void TheTrustedKeyEntryIsReadyToPasteAndCarriesTheWholeKey()
    {
        // ⭐ Generated rather than transcribed: a public key is 120-odd base64 characters nobody
        //    proof-reads, and one altered character produces a build that refuses every licence forever.
        var result = KeyCeremony.Perform("R1", Passphrase, Now);
        var expected = Convert.ToBase64String(result.PublicKey);

        var rejoined = result.TrustedKeyEntry
            .Replace("\r\n", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace("\" +    \"", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\"+\"", string.Empty, StringComparison.Ordinal);

        Assert.Contains(expected, rejoined, StringComparison.Ordinal);
        Assert.Contains("SignatureAlgorithm.EcdsaP256Sha256", result.TrustedKeyEntry, StringComparison.Ordinal);
        Assert.EndsWith("))," + Environment.NewLine, result.TrustedKeyEntry, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFingerprintIsStableAndDistinguishesKeys()
    {
        var a = KeyCeremony.Perform("R1", Passphrase, Now);
        var b = KeyCeremony.Perform("R1", Passphrase, Now);

        Assert.Equal(KeyCeremony.Fingerprint(a.PublicKey), KeyCeremony.Fingerprint(a.PublicKey));
        Assert.NotEqual(KeyCeremony.Fingerprint(a.PublicKey), KeyCeremony.Fingerprint(b.PublicKey));
        Assert.Equal(a.PublicKeyFingerprint, KeyCeremony.Fingerprint(a.PublicKey));
    }

    [Fact]
    public void ACeremonyWithoutAKeyIdIsRefused()
    {
        Assert.Throws<ArgumentException>(() => KeyCeremony.Perform(" ", Passphrase, Now));
        Assert.Throws<ArgumentException>(() => KeyCeremony.Perform("R1", string.Empty, Now));
    }
}
