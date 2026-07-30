using System;
using System.IO;
using System.Text;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;
using EmberTern.Core.Settings.Export;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Small I/O helpers shared by the etap 5a export tests.</summary>
internal static class SettingsExportTestIo
{
    /// <summary>The file's bytes as a stream — UTF-8 <b>without a BOM</b>, matching how an export is written. A
    /// BOM would put three bytes in front of the magic, which is exactly the sort of thing the byte comparison is
    /// there to catch.</summary>
    internal static Stream AsStream(string content)
        => new MemoryStream(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));

    /// <summary>Rewrites one tab-separated header field, so a test can craft a header a writer would never
    /// produce (a future version, an unknown scheme) without hand-building the whole file.</summary>
    internal static string WithHeaderField(string file, int fieldIndex, string value)
    {
        var newline = file.IndexOf('\n');
        var fields = file[..newline].Split('\t');
        fields[fieldIndex] = value;
        return string.Join('\t', fields) + file[newline..];
    }

    /// <summary>Counts how many bytes were actually pulled off the stream. ⭐ The instrument for the "check the
    /// magic from the STREAM, before loading the file" rule — without it that rule is untestable and would quietly
    /// stop holding the first time someone wrote the obvious <c>ReadAllText</c>.</summary>
    internal sealed class CountingStream : Stream
    {
        private readonly Stream _inner;

        internal CountingStream(Stream inner) => _inner = inner;

        internal long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int ReadByte()
        {
            var value = _inner.ReadByte();
            if (value >= 0) BytesRead++;
            return value;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>
/// Settings Center etap 5a — the export <b>format</b>: identity, the versioned envelope, the ordered check
/// sequence, and the crypto behaviour the sequence depends on.
///
/// <para>⭐ The tests here are mostly about <b>which message a user gets</b>, which sounds cosmetic and is not:
/// the ordered checks exist so that "you picked a PDF", "this file is from a newer build", "wrong passphrase" and
/// "this file is damaged" are four distinct answers with four different next actions. A design where they collapse
/// into one still "works" — it just makes every failure unsolvable.</para>
/// </summary>
public class SettingsExportFormatTests
{
    // Field positions in the tab-separated header line — see SettingsExportEnvelope.Wrap.
    private const int MagicField = 0;
    private const int FormatVersionField = 1;
    private const int AppVersionField = 2;
    private const int SchemeField = 3;
    private const int KdfField = 4;
    private const int IterationsField = 5;
    private const int SaltField = 6;

    // ─── IDENTITY (Q10 + Q13) ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheExportMagic_IsItsOwn_AndNotSettingsDats()
    {
        // ⭐ This single assertion is the whole of ratified decision Q13. If the two formats shared a magic, the
        // first check could not tell them apart, and a user who picked settings.dat in the import dialog would be
        // asked for a passphrase before being told it was wrong — about a file that never had one.
        Assert.NotEqual(SettingsFileContainer.Magic, SettingsExportFormat.Magic);

        // ⚠ And it must not merely differ: settings.dat's magic must not be a prefix that could match ours in a
        // sloppy comparison, nor ours a prefix of it. Ours deliberately EXTENDS it, so the prefix relationship
        // exists in one direction and the check has to be whole-token. That is what the next test pins.
        Assert.StartsWith(SettingsFileContainer.Magic, SettingsExportFormat.Magic, StringComparison.Ordinal);
    }

    [Fact]
    public void ASettingsDatFile_OfferedToImport_IsRejectedAtTheMagic()
    {
        // ⭐ The reason Q13 exists, as a test. Note the status: NotAnExportFile, resolved before any version,
        // scheme or passphrase — never "wrong passphrase" about a file that has none.
        var dir = NewTempDir();
        try
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(ExportFixtures.Populated());

            var inspection = SettingsImportReader.Inspect(store.FilePath);

            Assert.Equal(SettingsImportStatus.NotAnExportFile, inspection.Status);
            Assert.False(inspection.CanBeOpened);
            AssertNoPassphraseCanChangeThis(inspection);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void AnExportPutWhereSettingsDatBelongs_IsStillRefused_AndNowSaysWhichFileItIs()
    {
        // The mirror direction. It was always refused (its magic is not the container's, so it fell through to the
        // legacy-headerless path and failed to decrypt) — but with the DPAPI story attached, which was untrue.
        // Identity is decided in the store, so the truthful answer belongs there.
        var dir = NewTempDir();
        try
        {
            var store = new ApplicationSettingsStore(dir);
            File.WriteAllText(store.FilePath, SettingsExporter.Export(
                ExportFixtures.Populated(), new SettingsExportOptions(), ExportFixtures.Passphrase,
                ExportFixtures.AppVersion, ExportFixtures.Iterations));

            var load = store.LoadWithStatus();
            Assert.NotEqual(SettingsLoadStatus.Loaded, load.Status);
            Assert.Contains("exported", store.LastLoadDiagnostic!, StringComparison.OrdinalIgnoreCase);

            // ⚠ And the important half: it must not be overwritten. This is the user's portable copy of
            // everything — the file most worth not destroying.
            store.Save(new ApplicationSettings());
            Assert.NotNull(store.LastSaveDiagnostic);
            Assert.StartsWith(SettingsExportFormat.Magic, File.ReadAllText(store.FilePath), StringComparison.Ordinal);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Theory]
    [InlineData("PKbinary zip content")]
    [InlineData("%PDF-1.7\n%âãÏÓ binary")]
    [InlineData("")]
    [InlineData("EMBER")]
    [InlineData("just some notes I keep in a text file")]
    public void ABinaryOrUnrelatedFile_IsRejectedCleanly_NeverWithAnException(string content)
    {
        // ⚠ A crash here would be the one failure mode worse than the unclear message the magic replaced. Bytes
        // are compared, so a ZIP's PK\x03\x04 and a PDF's %PDF- never reach a decoder.
        var inspection = SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(content));

        Assert.Equal(SettingsImportStatus.NotAnExportFile, inspection.Status);
        Assert.Equal("This is not an EmberTern settings file.", inspection.Message);
        AssertNoPassphraseCanChangeThis(inspection);
    }

    [Fact]
    public void TheMagicIsCheckedFromTheStream_BeforeTheFileIsLoaded()
    {
        // ⭐ The practical half of Q10's rationale, and easy to lose by writing the obvious ReadAllText: picking a
        // 2 GB file by mistake must cost a handful of bytes, not a full read. Measured rather than asserted in
        // prose.
        var huge = new string('A', 4 * 1024 * 1024);
        var stream = new SettingsExportTestIo.CountingStream(SettingsExportTestIo.AsStream(huge));

        Assert.Equal(SettingsImportStatus.NotAnExportFile, SettingsImportReader.Inspect(stream).Status);
        Assert.True(stream.BytesRead <= SettingsExportFormat.Magic.Length,
            $"the magic must be decided from the first {SettingsExportFormat.Magic.Length} bytes, but "
            + $"{stream.BytesRead} were read.");
    }

    [Fact]
    public void AFileWithOurMagicButNoHeaderLine_IsDamagedRatherThanUnrecognised()
    {
        // ⭐ Two different problems deserve two different answers: "you picked the wrong file" and "your export is
        // broken" call for entirely different next actions. The bound is also what stops a magic-prefixed file
        // with no newline being pulled into memory whole.
        var noNewline = SettingsExportFormat.Magic + new string('x', SettingsExportFormat.MaxHeaderBytes * 2);

        Assert.Equal(SettingsImportStatus.Damaged,
            SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(noNewline)).Status);
    }

    [Fact]
    public void TheMagicMustBeAWholeToken_NotAPrefix()
    {
        var file = Export();
        var impostor = SettingsExportTestIo.WithHeaderField(file, MagicField, SettingsExportFormat.Magic + "X");

        Assert.Equal(SettingsImportStatus.Damaged,
            SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(impostor)).Status);
    }

    [Fact]
    public void AShortHeaderLine_IsDamaged()
    {
        var truncated = SettingsExportFormat.Magic + "\t1\t\n(payload)";

        Assert.Equal(SettingsImportStatus.Damaged,
            SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(truncated)).Status);
    }

    // ─── THE ENVELOPE ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheHeaderIsCleartext_AndCarriesEachFieldExactlyOnce()
    {
        // ⚠ The header being readable is not a compromise of "always encrypted" — it is what makes versioning
        // work at all. A wholly opaque file could not tell "an older export, migrate it" from "wrong passphrase".
        var file = Export();
        var firstLine = file[..file.IndexOf('\n')];
        var fields = firstLine.Split('\t');

        Assert.Equal(7, fields.Length);
        Assert.Equal(SettingsExportFormat.Magic, fields[MagicField]);
        Assert.Equal("1", fields[FormatVersionField]);
        Assert.Equal(ExportFixtures.AppVersion, fields[AppVersionField]);
        Assert.Equal(EncryptionSchemes.PassphraseAes256, fields[SchemeField]);
        Assert.Equal(PassphraseProtector.Pbkdf2Sha256, fields[KdfField]);
        Assert.Equal(ExportFixtures.Iterations.ToString(), fields[IterationsField]);
        Assert.NotEmpty(fields[SaltField]);
    }

    [Fact]
    public void TheSectionList_IsNotInTheCleartextHeader()
    {
        // ⚠ Tempting, so an import could preview contents before asking for a passphrase — but a cleartext
        // "contains: Connections, Passwords" advertises what is worth attacking.
        var file = Export(new SettingsExportOptions { Connections = true, Passwords = true });
        var header = file[..file.IndexOf('\n')];

        Assert.DoesNotContain(SettingsExportSections.Passwords, header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SettingsExportSections.Connections, header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryExportGetsItsOwnSalt()
    {
        // Reusing a salt across files would let one derived key open both. Per-file and random.
        Assert.NotEqual(Salt(Export()), Salt(Export()));
    }

    private static string Salt(string file) => file[..file.IndexOf('\n')].Split('\t')[SaltField];

    // ─── ORDERED CHECKS 2, 3, 4 — all before the passphrase ─────────────────────────────────

    [Fact]
    public void ANewerFormatVersion_IsRefusedNamingTheVersion()
    {
        var future = SettingsExportTestIo.WithHeaderField(Export(), FormatVersionField, "2");

        var inspection = SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(future));

        Assert.Equal(SettingsImportStatus.FutureFormatVersion, inspection.Status);
        Assert.Contains("v2", inspection.Message, StringComparison.Ordinal);
        Assert.Contains("newer EmberTern build", inspection.Message, StringComparison.Ordinal);
        AssertNoPassphraseCanChangeThis(inspection);
    }

    [Fact]
    public void AFormatVersionOlderThanAnyMigrationStep_IsRefusedRatherThanGuessedAt()
    {
        // ⭐ The stepwise ladder's load-bearing property, provable today. A version below the oldest we hold a
        // step for is refused — never silently accepted as current, which would import whatever happened to
        // deserialize and drop the rest. A partial import is worse than none (rule #11).
        //
        // ⚠ And it is refused HERE, from the header, before the passphrase: whether a step exists is a fact about
        // the version, not about the payload.
        var ancient = SettingsExportTestIo.WithHeaderField(Export(), FormatVersionField, "0");

        var inspection = SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(ancient));

        Assert.Equal(SettingsImportStatus.UnsupportedFormatVersion, inspection.Status);
        Assert.Contains("v0", inspection.Message, StringComparison.Ordinal);
        Assert.Contains($"v{SettingsExportFormat.OldestSupportedFormatVersion}", inspection.Message,
            StringComparison.Ordinal);
        AssertNoPassphraseCanChangeThis(inspection);
    }

    [Fact]
    public void TheCurrentFormatVersion_IsAcceptedAndNeedsNoMigration()
    {
        var inspection = SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(Export()));

        Assert.Equal(SettingsImportStatus.Ok, inspection.Status);
        Assert.Equal(SettingsExportFormat.CurrentFormatVersion, inspection.Header.FormatVersion);
        Assert.True(SettingsImportReader.Open(inspection, ExportFixtures.Passphrase).IsUsable);
    }

    [Theory]
    [InlineData(SchemeField, "aes256-somethingelse", "encryption scheme")]
    [InlineData(KdfField, "SCRYPT", "key-derivation function")]
    public void AnUnsupportedCryptoParameter_IsRefusedBeforeThePassphrase(
        int field, string value, string expectedInMessage)
    {
        var tweaked = SettingsExportTestIo.WithHeaderField(Export(), field, value);

        var inspection = SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(tweaked));

        Assert.Equal(SettingsImportStatus.UnsupportedEncryption, inspection.Status);
        Assert.Contains(expectedInMessage, inspection.Message, StringComparison.OrdinalIgnoreCase);
        AssertNoPassphraseCanChangeThis(inspection);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2000000000")]
    public void AnAbsurdIterationCount_IsRefusedRatherThanHonoured(string iterations)
    {
        // ⚠ A denial-of-service guard, not fussiness: the count sits in a cleartext header anyone can edit, and
        // honouring a claimed two billion would hang inside the KDF with no way out.
        var tweaked = SettingsExportTestIo.WithHeaderField(Export(), IterationsField, iterations);

        var inspection = SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(tweaked));

        Assert.Equal(SettingsImportStatus.UnsupportedEncryption, inspection.Status);
        Assert.Contains(iterations, inspection.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonNumericVersionOrIterations_IsDamagedNotUnsupported()
    {
        Assert.Equal(SettingsImportStatus.Damaged, SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(
            SettingsExportTestIo.WithHeaderField(Export(), FormatVersionField, "one"))).Status);
        Assert.Equal(SettingsImportStatus.Damaged, SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(
            SettingsExportTestIo.WithHeaderField(Export(), IterationsField, "lots"))).Status);
        Assert.Equal(SettingsImportStatus.Damaged, SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(
            SettingsExportTestIo.WithHeaderField(Export(), SaltField, "not base64!!"))).Status);
    }

    [Fact]
    public void TheAppVersion_IsCarriedForDiagnostics_AndNothingBranchesOnIt()
    {
        // ⛔ AppVersion is diagnostics only — the shape gotcha #289 burned this project on. A nonsense value must
        // therefore change NOTHING about whether the file reads.
        var nonsense = SettingsExportTestIo.WithHeaderField(Export(), AppVersionField, "not-a-version");

        var inspection = SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(nonsense));

        Assert.Equal(SettingsImportStatus.Ok, inspection.Status);
        Assert.Equal("not-a-version", inspection.Header.AppVersion);
        Assert.True(SettingsImportReader.Open(inspection, ExportFixtures.Passphrase).IsUsable);
    }

    [Fact]
    public void TheAppVersionComesFromTheCaller_NeverFromCore()
    {
        // ⭐ The seam: Core cannot see AppInfo (App references Core, not the reverse), so the version travels in
        // as a parameter. That is also the right shape for a field nothing may branch on — Core cannot condition
        // on what it never computes. ⛔ Never a literal fallback here.
        Assert.Equal("1.2.3", Header(Export(appVersion: "1.2.3")).AppVersion);
        Assert.Equal("4.5.6", Header(Export(appVersion: "4.5.6")).AppVersion);
    }

    // ─── CHECKS 5 AND 6 — the passphrase, and why the format uses GCM ───────────────────────

    [Fact]
    public void AWrongPassphrase_IsDistinguishableFromADamagedFile()
    {
        // ⭐ The whole reason for AES-GCM rather than CBC, as a test. Under CBC a wrong key yields garbage that
        // then fails JSON parsing, and the user is told "corrupt file" when the truth was "wrong passphrase" —
        // the same distinction SettingsLoadStatus draws between Corrupt and Unreadable, and for the same reason:
        // the two have different prognoses and the user's next action differs.
        var file = Export();

        var wrongPassphrase = SettingsImportReader.Open(Inspect(file), "not the passphrase");
        Assert.Equal(SettingsImportStatus.WrongPassphrase, wrongPassphrase.Status);
        Assert.Contains("passphrase", wrongPassphrase.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(wrongPassphrase.Content);

        // A genuinely damaged payload — truncated, so it is not even a well-formed blob.
        var damaged = file[..(file.IndexOf('\n') + 6)];
        var damagedResult = SettingsImportReader.Open(Inspect(damaged), ExportFixtures.Passphrase);
        Assert.Equal(SettingsImportStatus.Damaged, damagedResult.Status);

        Assert.NotEqual(wrongPassphrase.Status, damagedResult.Status);
    }

    [Fact]
    public void AModifiedPayload_IsDetected()
    {
        // The GCM authentication tag. ⚠ GCM cannot distinguish a wrong key from a modified payload, and neither
        // do we claim to — the message names both possibilities. What matters is that tampering is never silently
        // accepted.
        var file = Export();
        var newline = file.IndexOf('\n');
        var blob = Convert.FromBase64String(file[(newline + 1)..]);

        // Flip a bit well past the nonce and tag, i.e. inside the ciphertext itself, so this is an authentication
        // failure rather than a malformed-blob failure.
        blob[^1] ^= 0x01;
        var tampered = file[..(newline + 1)] + Convert.ToBase64String(blob);

        var result = SettingsImportReader.Open(Inspect(tampered), ExportFixtures.Passphrase);

        Assert.Equal(SettingsImportStatus.WrongPassphrase, result.Status);
        Assert.Contains("modified", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Content);
    }

    [Fact]
    public void AnEmptyPassphrase_IsRefusedWithoutAttemptingAnything()
    {
        Assert.Equal(SettingsImportStatus.WrongPassphrase,
            SettingsImportReader.Open(Inspect(Export()), string.Empty).Status);
    }

    [Fact]
    public void OpenCannotBeUsedToSkipTheInspection()
    {
        // ⚠ The ordering corollary for etap 5b, made structural: Open takes an inspection, not a path, so the
        // passphrase dialog cannot become the entry point to import. This pins the belt-and-braces half — a
        // failed inspection handed to Open returns the inspection's own answer rather than trying to decrypt.
        var inspection = SettingsImportReader.Inspect(SettingsExportTestIo.AsStream("%PDF-1.7"));

        var result = SettingsImportReader.Open(inspection, ExportFixtures.Passphrase);

        Assert.Equal(SettingsImportStatus.NotAnExportFile, result.Status);
        Assert.Equal(inspection.Message, result.Message);
    }

    // ─── THE SETTINGS-SHAPE AXIS (the third version) ────────────────────────────────────────

    [Fact]
    public void AnOlderSettingsSchema_IsMigratedByTheExistingLadder()
    {
        // ⭐ F4's answer, as a test. The payload is shaped as an ApplicationSettings precisely so that an import
        // calls ApplicationSettingsStore.MigrateToCurrentVersion — the same method LoadWithStatus calls — rather
        // than growing a second migration path. A future Migrate_2_3 therefore applies to imports for free.
        var old = ExportFixtures.Populated();
        old.SchemaVersion = 1;

        var result = SettingsImportReader.Open(Inspect(SettingsExporter.Export(
            old, new SettingsExportOptions(), ExportFixtures.Passphrase, ExportFixtures.AppVersion,
            ExportFixtures.Iterations)), ExportFixtures.Passphrase);

        Assert.Equal(SettingsImportStatus.Ok, result.Status);
        Assert.Equal(ApplicationSettingsStore.CurrentSchemaVersion, result.Content!.Settings.SchemaVersion);
    }

    [Fact]
    public void ANewerSettingsSchema_IsRefusedOnItsOwnTerms()
    {
        // ⚠ The third axis, refused separately and with its own message. ⛔ It must never be folded into the
        // format version: doing so would tie "we added a section to the export" to "the settings shape changed",
        // forcing a schema bump that makes older builds refuse the whole settings.dat.
        var future = ExportFixtures.Populated();
        future.SchemaVersion = ApplicationSettingsStore.CurrentSchemaVersion + 7;

        var result = SettingsImportReader.Open(Inspect(SettingsExporter.Export(
            future, new SettingsExportOptions(), ExportFixtures.Passphrase, ExportFixtures.AppVersion,
            ExportFixtures.Iterations)), ExportFixtures.Passphrase);

        Assert.Equal(SettingsImportStatus.FutureSettingsSchema, result.Status);
        Assert.Contains($"v{future.SchemaVersion}", result.Message, StringComparison.Ordinal);
        Assert.Null(result.Content);
    }

    [Fact]
    public void ImportNormalizesPreferences_BecauseThisIsAFileBoundaryLikeAnyOther()
    {
        // Silent and total, exactly as PreferencesStore.Load is: a preference value from a build that knew more
        // options becomes this build's default rather than failing the import.
        var mangled = ExportFixtures.Populated();
        mangled.UserSettings.Preferences = new Preferences { Theme = "Solarized", Language = "kl" };

        var result = SettingsImportReader.Open(Inspect(SettingsExporter.Export(
            mangled, new SettingsExportOptions(), ExportFixtures.Passphrase, ExportFixtures.AppVersion,
            ExportFixtures.Iterations)), ExportFixtures.Passphrase);

        Assert.Equal(SettingsImportStatus.Ok, result.Status);
        Assert.Equal(PreferenceOptions.Theme.Default, result.Content!.Settings.UserSettings.Preferences.Theme);
        Assert.Equal(PreferenceOptions.Language.Default, result.Content.Settings.UserSettings.Preferences.Language);
    }

    // ─── THE PROTECTOR ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheProtectorRoundTrips_AndReportsItsScheme()
    {
        var salt = PassphraseProtector.NewSalt();
        var protector = PassphraseProtector.Create("pass", salt, ExportFixtures.Iterations);

        Assert.Equal(EncryptionSchemes.PassphraseAes256, protector.Scheme);
        Assert.Equal("payload", protector.Unprotect(protector.Protect("payload")));
    }

    [Fact]
    public void EachEncryptionUsesAFreshNonce_SoTheSamePlaintextNeverProducesTheSameBlob()
    {
        var protector = PassphraseProtector.Create("pass", PassphraseProtector.NewSalt(), ExportFixtures.Iterations);

        Assert.NotEqual(protector.Protect("same"), protector.Protect("same"));
    }

    [Fact]
    public void TheProtectorRefusesParametersThatCouldNotProduceAReadableFile()
    {
        var salt = PassphraseProtector.NewSalt();

        Assert.Throws<ArgumentException>(() =>
            PassphraseProtector.Create(string.Empty, salt, ExportFixtures.Iterations));
        Assert.Throws<ArgumentException>(() =>
            PassphraseProtector.Create("pass", Array.Empty<byte>(), ExportFixtures.Iterations));
        Assert.Throws<ArgumentException>(() =>
            PassphraseProtector.Create("pass", salt, PassphraseProtector.MaxIterations + 1));
        Assert.Throws<ArgumentException>(() =>
            PassphraseProtector.Create("pass", salt, ExportFixtures.Iterations, kdf: "SCRYPT"));
    }

    [Fact]
    public void ThePassphraseSchemeIsNotAsettingsDatScheme()
    {
        // ⚠ The deviation from the reserved comment's instruction, pinned so it is not "fixed" back. Registering
        // aes256-passphrase in ApplicationSettingsStore.ResolveProtector would mean returning a protector with no
        // passphrase — one that cannot decrypt — turning an honest refusal into a misleading "could not be
        // decrypted". A settings.dat declaring this scheme is refused, and refusal is the correct outcome.
        var dir = NewTempDir();
        try
        {
            var store = new ApplicationSettingsStore(dir);
            File.WriteAllText(store.FilePath, SettingsFileContainer.Wrap(
                SettingsFileContainer.CurrentContainerVersion, EncryptionSchemes.PassphraseAes256, "cGF5bG9hZA=="));

            Assert.NotEqual(SettingsLoadStatus.Loaded, store.LoadWithStatus().Status);

            store.Save(new ApplicationSettings());
            Assert.NotNull(store.LastSaveDiagnostic);
        }
        finally
        {
            Delete(dir);
        }
    }

    // ─── ROUND TRIP THROUGH A REAL FILE ─────────────────────────────────────────────────────

    [Fact]
    public void ExportToAFileAndBack_WithProductionKdfIterations()
    {
        // Everything else here uses a low iteration count, which is the design working (the file states what it
        // used). This one exercise runs the real default once, so the shipped parameters are known to work rather
        // than only the test ones.
        var dir = NewTempDir();
        try
        {
            // NewTempDir only names a directory; ApplicationSettingsStore creates its own, this test writes
            // straight to the filesystem.
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "backup" + SettingsExportFormat.FileExtension);
            SettingsExporter.ExportTo(path, ExportFixtures.Populated(), new SettingsExportOptions(),
                ExportFixtures.Passphrase, ExportFixtures.AppVersion);

            // ⚠ UTF-8 with no BOM: the magic must be the literal first bytes of the file.
            Assert.Equal(Encoding.UTF8.GetBytes(SettingsExportFormat.Magic)[0], File.ReadAllBytes(path)[0]);

            var inspection = SettingsImportReader.Inspect(path);
            Assert.Equal(SettingsImportStatus.Ok, inspection.Status);
            Assert.Equal(PassphraseProtector.DefaultIterations, inspection.Header.Iterations);

            var result = SettingsImportReader.Open(inspection, ExportFixtures.Passphrase);
            Assert.True(result.IsUsable);
            Assert.Equal("Lab", result.Content!.Settings.Connections[0].Name);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void AMissingFile_IsUnreadableRatherThanAnException()
    {
        var inspection = SettingsImportReader.Inspect(
            Path.Combine(Path.GetTempPath(), "EmberTern-nope-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(SettingsImportStatus.Unreadable, inspection.Status);
        Assert.NotEmpty(inspection.Message);
    }

    // ─── helpers ────────────────────────────────────────────────────────────────────────────

    // ⭐ The assertion that proves checks 1–4 really are resolved BEFORE a credential is requested: if no
    // passphrase can change the outcome, then asking for one would have been asking for nothing.
    private static void AssertNoPassphraseCanChangeThis(SettingsImportInspection inspection)
    {
        Assert.False(inspection.CanBeOpened);
        foreach (var attempt in new[] { ExportFixtures.Passphrase, "another one", string.Empty })
        {
            var result = SettingsImportReader.Open(inspection, attempt);
            Assert.Equal(inspection.Status, result.Status);
            Assert.Null(result.Content);
        }
    }

    private static string Export(SettingsExportOptions? options = null, string? appVersion = null)
        => SettingsExporter.Export(ExportFixtures.Populated(), options ?? new SettingsExportOptions(),
            ExportFixtures.Passphrase, appVersion ?? ExportFixtures.AppVersion, ExportFixtures.Iterations);

    private static SettingsImportInspection Inspect(string file)
        => SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(file));

    private static SettingsExportHeader Header(string file) => Inspect(file).Header;

    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));

    private static void Delete(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
