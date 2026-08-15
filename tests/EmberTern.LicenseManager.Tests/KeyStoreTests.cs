using System;
using System.Text;
using System.Text.Json.Nodes;
using EmberTern.Licensing;
using EmberTern.Licensing.Issuing;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The encrypted store that holds the signing key.
///
/// <para>⭐ The load-bearing behaviour here is not "it round-trips" — it is that <b>a wrong passphrase and
/// a damaged file are told apart</b>. The operator's next action differs completely (retype, versus reach
/// for the offline backup), and under an unauthenticated cipher the two would be indistinguishable. That
/// is why the keystore uses GCM, and <see cref="AWrongPassphraseIsNotACorruptFile"/> is what holds it.</para>
/// </summary>
public sealed class KeyStoreTests
{
    private const string Passphrase = "correct horse battery staple regard drift";
    private static readonly DateTimeOffset Created = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private static byte[] NewStore(string passphrase = Passphrase, string keyId = "R1")
    {
        var entry = KeyStoreEntry.Generate(keyId, Created);
        return KeyStore.Create([entry], passphrase);
    }

    [Fact]
    public void AKeystoreRoundTrips()
    {
        var file = NewStore();

        using var store = KeyStore.Open(file, Passphrase);

        Assert.Single(store.Entries);
        Assert.Equal("R1", store.Entries[0].KeyId);
        Assert.Equal(SignatureAlgorithm.EcdsaP256Sha256, store.Entries[0].Algorithm);
        Assert.Equal(Created, store.Entries[0].CreatedAt);
        Assert.False(store.Entries[0].Retired);
    }

    [Fact]
    public void TheKeySurvivesTheRoundTripUnchanged()
    {
        // ⭐ The property that actually matters: not that a key comes back, but that it is THE SAME key.
        //    A store that silently returned a different valid key would pass every other test here and be
        //    catastrophic — it would renew nothing that is already in the field.
        var entry = KeyStoreEntry.Generate("R1", Created);
        var expected = entry.ExportPublicKey();
        var file = KeyStore.Create([entry], Passphrase);

        using var store = KeyStore.Open(file, Passphrase);
        using var key = store.Unlock("R1");

        Assert.Equal(expected, key.ExportPublicKey());
    }

    [Fact]
    public void AWrongPassphraseIsNotACorruptFile()
    {
        var file = NewStore();

        var wrong = Assert.Throws<KeyStoreException>(() => KeyStore.Open(file, "not the passphrase"));
        Assert.Equal(KeyStoreFailure.WrongPassphrase, wrong.Failure);

        var empty = Assert.Throws<KeyStoreException>(() => KeyStore.Open(file, string.Empty));
        Assert.Equal(KeyStoreFailure.WrongPassphrase, empty.Failure);
    }

    [Fact]
    public void ATamperedPayloadFailsAuthentication()
    {
        var tampered = Mutate(NewStore(), header =>
        {
            var payload = header["payload"]!.GetValue<string>();
            header["payload"] = payload[0] == 'A' ? 'B' + payload[1..] : 'A' + payload[1..];
        });

        var exception = Assert.Throws<KeyStoreException>(() => KeyStore.Open(tampered, Passphrase));
        Assert.Equal(KeyStoreFailure.WrongPassphrase, exception.Failure);
    }

    [Theory]
    [InlineData("{}", KeyStoreFailure.NotAKeyStore)]
    [InlineData("not json", KeyStoreFailure.NotAKeyStore)]
    [InlineData("""{"magic":"SOMETHING-ELSE","version":1}""", KeyStoreFailure.NotAKeyStore)]
    public void SomethingThatIsNotAKeystoreSaysSo(string content, KeyStoreFailure expected)
    {
        var exception = Assert.Throws<KeyStoreException>(
            () => KeyStore.Open(Encoding.UTF8.GetBytes(content), Passphrase));

        Assert.Equal(expected, exception.Failure);
    }

    [Fact]
    public void AFutureVersionIsRefusedRatherThanPartiallyRead()
    {
        var file = Mutate(NewStore(), header => header["version"] = 99);

        var exception = Assert.Throws<KeyStoreException>(() => KeyStore.Open(file, Passphrase));
        Assert.Equal(KeyStoreFailure.UnsupportedVersion, exception.Failure);
    }

    [Theory]
    [InlineData("scheme", "rot13")]
    [InlineData("kdf", "scrypt")]
    public void AnUnknownSchemeOrKdfIsRefusedBeforeDecryptionIsAttempted(string field, string value)
    {
        // ⭐ Why the header is cleartext: without it, an unreadable future file and a wrong passphrase are
        //    the same event, and the operator is told to retype something that will never work.
        var file = Mutate(NewStore(), header => header[field] = value);

        var exception = Assert.Throws<KeyStoreException>(() => KeyStore.Open(file, Passphrase));
        Assert.Equal(KeyStoreFailure.UnsupportedScheme, exception.Failure);
    }

    [Theory]
    [InlineData(2_000_000_000)]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnIterationCountOutsideTheAcceptedRangeIsMalformed(int iterations)
    {
        // ⚠ The upper bound is a denial-of-service guard: the count sits in a header anyone can edit, and
        //    honouring two billion iterations would hang inside the KDF with no way out.
        var file = Mutate(NewStore(), header => header["iterations"] = iterations);

        var exception = Assert.Throws<KeyStoreException>(() => KeyStore.Open(file, Passphrase));
        Assert.Equal(KeyStoreFailure.Corrupt, exception.Failure);
    }

    [Fact]
    public void ATruncatedPayloadIsCorrupt()
    {
        var file = Mutate(NewStore(), header => header["payload"] = "AAAA");

        var exception = Assert.Throws<KeyStoreException>(() => KeyStore.Open(file, Passphrase));
        Assert.Equal(KeyStoreFailure.Corrupt, exception.Failure);
    }

    [Fact]
    public void AnEmptyPassphraseCannotProduceAKeystore()
    {
        // ⛔ Makes "the signing key is always encrypted at rest" unrepresentable to violate rather than
        //    merely documented.
        var entry = KeyStoreEntry.Generate("R1", Created);

        Assert.Throws<ArgumentException>(() => KeyStore.Create([entry], string.Empty));
    }

    [Fact]
    public void DuplicateKeyIdsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => KeyStore.Create(
            [KeyStoreEntry.Generate("R1", Created), KeyStoreEntry.Generate("R1", Created)], Passphrase));
    }

    [Fact]
    public void AnEmptyKeystoreIsNotAKeystore()
    {
        Assert.Throws<ArgumentException>(() => KeyStore.Create([], Passphrase));
    }

    [Fact]
    public void ThePassphraseCanBeChangedWithoutChangingTheKey()
    {
        var entry = KeyStoreEntry.Generate("R1", Created);
        var expected = entry.ExportPublicKey();
        var original = KeyStore.Create([entry], Passphrase);

        byte[] rekeyed;
        using (var store = KeyStore.Open(original, Passphrase))
        {
            rekeyed = store.Save("a completely different passphrase entirely");
        }

        Assert.Throws<KeyStoreException>(() => KeyStore.Open(rekeyed, Passphrase));

        using var reopened = KeyStore.Open(rekeyed, "a completely different passphrase entirely");
        using var key = reopened.Unlock("R1");
        Assert.Equal(expected, key.ExportPublicKey());
    }

    [Fact]
    public void UnlockingAKeyThatIsNotThereSaysSo()
    {
        using var store = KeyStore.Open(NewStore(), Passphrase);

        var exception = Assert.Throws<KeyStoreException>(() => store.Unlock("R2"));
        Assert.Equal(KeyStoreFailure.KeyNotFound, exception.Failure);
    }

    [Fact]
    public void EveryKeystoreGetsItsOwnSaltAndNonce()
    {
        // Two stores over the SAME key and the SAME passphrase must not produce identical bytes — a
        // repeated salt or nonce is the classic way an authenticated cipher stops being one.
        var entry = KeyStoreEntry.Generate("R1", Created);

        Assert.NotEqual(KeyStore.Create([entry], Passphrase), KeyStore.Create([entry], Passphrase));
    }

    [Fact]
    public void AClosedStoreRefusesToBeUsed()
    {
        var store = KeyStore.Open(NewStore(), Passphrase);
        store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => store.Unlock("R1"));
        Assert.Throws<ObjectDisposedException>(() => store.Save(Passphrase));
    }

    /// <summary>
    /// Edits the cleartext header of a keystore.
    ///
    /// <para>⚠ <b>It parses and rewrites the JSON rather than doing a text replace, and that is not
    /// fastidiousness — the first version of this helper did a text replace and two tests failed for a
    /// reason that had nothing to do with the product.</b> <c>Utf8JsonWriter</c>'s default encoder escapes
    /// <c>+</c> as <c>+</c>, so the base64 payload as it appears in the FILE differs from the string
    /// <c>GetString()</c> hands back, and <c>Replace</c> silently matched nothing. The mutation never
    /// happened, the keystore opened perfectly, and the tests reported "no exception was thrown" — a
    /// green product wearing a red test.</para>
    /// </summary>
    private static byte[] Mutate(byte[] file, Action<JsonObject> mutate)
    {
        var header = (JsonObject)JsonNode.Parse(file)!;
        mutate(header);
        return Encoding.UTF8.GetBytes(header.ToJsonString());
    }
}
