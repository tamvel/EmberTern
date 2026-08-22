using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EmberTern.Licensing;
using EmberTern.Licensing.Issuing;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>The ceremony's executor (L7.1): the three values it has to leave behind, and the one operation
/// that turns a backup from a hypothesis into a verified copy.</b>
///
/// <para><c>KeyCeremonyTests</c> already proves the ceremony functions themselves. What was missing was a
/// path from those functions to the operator: <c>SigningSession.Create</c> called
/// <c>KeyCeremony.Perform</c> and kept only the keystore bytes, so the fingerprint, the public key and the
/// paste-ready entry existed for the duration of one method call. §24.1 steps 5 and 7 — verify the restore,
/// record the fingerprint — therefore had no executor anywhere in the application.</para>
///
/// <para>⛔ Nothing here performs a production ceremony, and nothing here touches
/// <c>TrustedKeys.Production</c>: every key is the fixture's own, generated per test into a temporary
/// folder.</para>
/// </summary>
public sealed class CeremonyMechanismTests
{
    private const string OtherPassphrase = "another six generated words on paper";

    /// <summary>
    /// ⭐⭐ <b>The value shown on screen is the value that makes a real licence verify.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ This is the claim the ceremony rests on, and it is not "the string looks like a key": the public
    /// key read off the surface is put into a <see cref="TrustedKeyTable"/> exactly as the client would
    /// hold it, and a licence signed by the live session is verified against it through the real
    /// <see cref="LicenseVerifier"/>. ⛔ A fingerprint or an entry that agreed with itself but not with the
    /// signing key would pass a weaker test and fail every customer.
    /// </remarks>
    [Fact]
    public void TheFactsOnScreen_AreTheKeyThatSignsAndVerifies()
    {
        using var manager = new ManagerFixture();
        var facts = SigningKeyFacts.Of(manager.Session);

        Assert.Equal(manager.Session.KeyId, facts.KeyId);
        Assert.Equal(SignatureAlgorithm.EcdsaP256Sha256, facts.Algorithm);

        // The entry is generated rather than transcribed, and it carries the whole key plus its id.
        Assert.Contains(facts.PublicKeyBase64[..40], facts.TrustedKeyEntry, StringComparison.Ordinal);
        Assert.Contains("\"" + facts.KeyId + "\"", facts.TrustedKeyEntry, StringComparison.Ordinal);

        var licence = manager.Session.Issuer.Issue(
            new LicenseTerms
            {
                Licensee = "Ceremony surface probe",
                Seats = 1,
                NotBefore = manager.Now,
                ExpiresAt = manager.Now.AddYears(1),
            },
            manager.Now);

        // ⭐ Built from the SHOWN base64, through Convert.FromBase64String — the same call the generated
        //   entry makes in the client.
        var asTheClientWouldHoldIt = new TrustedKeyTable(
        [
            new TrustedKey(
                facts.KeyId, facts.Algorithm, Convert.FromBase64String(facts.PublicKeyBase64)),
        ]);

        var verdict = LicenseVerifier.Verify(
            licence.ArmoredText,
            new LicenseVerificationContext(
                asTheClientWouldHoldIt,
                manager.Now,
                LicenseConstants.ProductId,
                LicenseConstants.MaxSupportedPayloadVersion,
                LicenseConstants.DefaultGracePeriod,
                BuildReleaseDate: null));

        Assert.Equal(LicenseStatus.Valid, verdict.Status);

        // The fingerprint is over that same key material, so two machines comparing it are comparing this.
        Assert.Equal(KeyCeremony.Fingerprint(Convert.FromBase64String(facts.PublicKeyBase64)),
            facts.Fingerprint);
    }

    /// <summary>
    /// ⭐⭐ <b>A backup of the WRONG key is detected, and no caller can weaken that.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <c>KeyCeremony.VerifyRestore</c> takes the <i>expected</i> public key, and that argument is what
    /// makes it a real check — without it the operation proves only that the file holds <i>a</i> working
    /// key, which a backup of the wrong key satisfies while being as useless as no backup at all (§35.2).
    /// ⭐ So the guard is on the SHAPE as well as the behaviour: <see cref="SigningKeyFacts.VerifyBackup"/>
    /// has no parameter for the expected key, so there is no call site that can pass the wrong one.
    /// </remarks>
    [Fact]
    public void VerifyBackup_BindsTheExpectedKeyAndCannotBeToldOtherwise()
    {
        var verify = typeof(SigningKeyFacts).GetMethod(nameof(SigningKeyFacts.VerifyBackup))!;
        var parameters = verify.GetParameters().Select(p => p.Name ?? string.Empty).ToArray();

        Assert.Equal<string>(["keyStoreFile", "passphrase", "now"], parameters);

        using var manager = new ManagerFixture();
        var facts = SigningKeyFacts.Of(manager.Session);

        // A real backup of THIS key: the active keystore's own bytes.
        var good = facts.VerifyBackup(
            File.ReadAllBytes(manager.Paths.KeyStore), ManagerFixture.Passphrase, manager.Now);

        Assert.True(good.Succeeded);
        Assert.True(good.PublicKeyMatches);

        // ⚠ A DIFFERENT key wearing the SAME key id — the case a fingerprint comparison exists for.
        var impostor = KeyCeremony.Perform(facts.KeyId, OtherPassphrase, manager.Now);
        var wrongKey = facts.VerifyBackup(impostor.KeyStoreFile, OtherPassphrase, manager.Now);

        Assert.False(wrongKey.Succeeded);
        Assert.True(wrongKey.Opened);
        Assert.True(wrongKey.KeyPresent);
        Assert.False(wrongKey.PublicKeyMatches);
        Assert.NotEqual(facts.Fingerprint, KeyCeremony.Fingerprint(impostor.PublicKey));
    }

    /// <summary>
    /// ⛔ The ceremony surface holds public key material and cannot sign.
    /// </summary>
    /// <remarks>
    /// ⭐ The same guard as <c>LicenseIssuerTests.NoPublicApiHandsOutPrivateKeyMaterial</c>, one layer out:
    /// L7.1 put key facts on a UI surface, and the property worth pinning is that doing so handed the
    /// surface nothing it could sign with. ⚠ It also asserts the raw bytes are not exposed — a
    /// <c>byte[]</c> property would be a handle onto the array a fingerprint is computed over.
    /// </remarks>
    [Fact]
    public void TheCeremonySurface_HoldsNothingThatCanSign()
    {
        var members = typeof(SigningKeyFacts)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.DeclaringType == typeof(SigningKeyFacts))
            .ToList();

        Assert.DoesNotContain(members, m => m.Name.Contains("Sign", StringComparison.Ordinal)
            && m.Name != nameof(SigningKeyFacts.VerifyBackup));

        Assert.DoesNotContain(
            typeof(SigningKeyFacts).GetProperties(),
            p => p.PropertyType == typeof(byte[]));

        // ⭐ And the view model that shows them cannot reach an issuer either: it never sees a session.
        var storageFields = typeof(StorageViewModel)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(f => f.FieldType)
            .ToList();

        Assert.DoesNotContain(typeof(SigningSession), storageFields);
        Assert.DoesNotContain(typeof(LicenseIssuer), storageFields);
        Assert.DoesNotContain(typeof(IssuingKey), storageFields);
    }

    /// <summary>
    /// ⭐ Four reachable outcomes, four different sentences — because the four mean four different things
    /// to do next.
    /// </summary>
    /// <remarks>
    /// <para>⚠ The fifth arm — <i>"holds the right key, but the licence it signed did not verify"</i> — is
    /// deliberately NOT driven here. Reaching it means a key that signs but whose signature does not
    /// verify, i.e. a broken platform or a broken verifier; fabricating it would mean stubbing the
    /// cryptography this stage was told not to touch. ⛔ Recorded as unproven-by-test rather than
    /// quietly counted as covered.</para>
    ///
    /// <para>⚠ It also pins that the message never carries <c>RestoreVerification.Detail</c>, whose own
    /// contract says it is English, for a log, and never for a screen.</para>
    /// </remarks>
    [Fact]
    public async Task EachVerificationOutcome_SaysSomethingDifferent()
    {
        using var manager = new ManagerFixture();
        var folder = Path.Combine(Path.GetTempPath(), "etlm-ceremony-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var model = new StorageViewModel(
                manager.Register, manager.Paths, SigningKeyFacts.Of(manager.Session), () => manager.Now);

            var said = new List<string>();

            async Task<string> Verify(string file, string passphrase)
            {
                model.OpenKeystorePicker = () => Task.FromResult<string?>(file);
                model.KeystoreBackupPassphrase = passphrase;
                await model.VerifyKeystoreBackupCommand.ExecuteAsync(null);

                said.Add(model.MessageText);
                return model.MessageText;
            }

            string Write(string name, byte[] bytes)
            {
                var path = Path.Combine(folder, name);
                File.WriteAllBytes(path, bytes);
                return path;
            }

            var facts = SigningKeyFacts.Of(manager.Session);

            // 1 — a real backup of this key.
            var usable = await Verify(manager.Paths.KeyStore, ManagerFixture.Passphrase);
            Assert.True(model.IsSuccess);
            Assert.Contains(facts.Fingerprint, usable, StringComparison.Ordinal);

            // ⭐ Cleared only on success — a failed attempt must not make the operator retype six words.
            Assert.Empty(model.KeystoreBackupPassphrase);

            // 2 — the passphrase does not open it.
            await Verify(manager.Paths.KeyStore, "definitely not the right words");
            Assert.True(model.IsError);
            Assert.Equal("definitely not the right words", model.KeystoreBackupPassphrase);

            // 3 — it opens, but holds a different key id.
            var otherId = Write(
                "other-id.etkeys",
                KeyCeremony.Perform("R2", OtherPassphrase, manager.Now).KeyStoreFile);
            await Verify(otherId, OtherPassphrase);
            Assert.True(model.IsError);

            // 4 — ⚠ the dangerous one: same key id, DIFFERENT key.
            var impostor = Write(
                "impostor.etkeys",
                KeyCeremony.Perform(facts.KeyId, OtherPassphrase, manager.Now).KeyStoreFile);
            await Verify(impostor, OtherPassphrase);
            Assert.True(model.IsError);

            Assert.Equal(4, said.Distinct(StringComparer.Ordinal).Count());

            // ⛔ None of them is the English diagnostic detail.
            Assert.DoesNotContain(said, text => text.Contains("keystore opened", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
