using System;
using System.Linq;
using System.Reflection;
using System.Text;
using EmberTern.Licensing.Issuing;
using EmberTern.LicenseManager.Data;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The encrypted container around a register snapshot.
///
/// <para>⭐ Every failure mode is asserted for its REASON, not merely for throwing: "you mistyped" and
/// "this file is damaged" send an operator to different places, and a container that cannot tell them
/// apart sends everyone to the backup they may not have.</para>
/// </summary>
public sealed class RegisterBackupTests
{
    private const string Passphrase = "six generated words kept on paper too";

    // ⚠ 1 iteration, not 600 000. The production work factor is deliberately expensive; paying it in
    //   every test here would add minutes for no coverage. The DEFAULT is asserted separately, below.
    private const int FastIterations = 1;

    private static readonly byte[] Snapshot = Encoding.UTF8.GetBytes("SQLite format 3\0 …pretend register…");

    [Fact]
    public void ABackupRoundTripsToTheExactBytesItWasGiven()
    {
        var file = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);

        Assert.Equal(Snapshot, RegisterBackup.Open(file, Passphrase));
    }

    [Fact]
    public void TheHeaderIsReadableWithoutThePassphrase()
    {
        var file = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);

        var header = RegisterBackup.ReadHeader(file);

        Assert.Equal(RegisterBackup.CurrentVersion, header.Version);
        Assert.Equal(Stamp, header.CreatedAt);
        Assert.Equal(2, header.SchemaVersion);
        Assert.Equal(FastIterations, header.Iterations);
    }

    [Fact]
    public void TheSnapshotIsNotReadableInTheFile()
    {
        var plain = Encoding.UTF8.GetBytes("ACME Sp. z o.o. secret register contents");
        var file = RegisterBackup.Create(plain, Passphrase, Stamp, 2, FastIterations);

        Assert.DoesNotContain("ACME", Encoding.UTF8.GetString(file), StringComparison.Ordinal);
    }

    [Fact]
    public void AWrongPassphraseIsRefusedAsAWrongPassphrase()
    {
        var file = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);

        var error = Assert.Throws<BackupException>(() => RegisterBackup.Open(file, "not the passphrase"));

        Assert.Equal(BackupFailure.WrongPassphrase, error.Failure);
    }

    [Fact]
    public void AnEmptyPassphraseIsRefusedOnBothSides()
    {
        Assert.Throws<ArgumentException>(
            () => RegisterBackup.Create(Snapshot, string.Empty, Stamp, 2, FastIterations));

        var file = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);
        Assert.Equal(
            BackupFailure.WrongPassphrase,
            Assert.Throws<BackupException>(() => RegisterBackup.Open(file, string.Empty)).Failure);
    }

    [Fact]
    public void AnEmptySnapshotIsNotABackup()
    {
        // ⚠ Zero bytes is not "a backup of an empty register" — an empty register is still a SQLite file
        //   with a schema. It means the snapshot step produced nothing.
        Assert.Throws<ArgumentException>(
            () => RegisterBackup.Create([], Passphrase, Stamp, 2, FastIterations));
    }

    [Fact]
    public void AModifiedPayloadIsRefused()
    {
        var file = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);

        // Flip a base64 character in the payload, well past the header line.
        var index = file.Length - 8;
        file[index] = file[index] == (byte)'A' ? (byte)'B' : (byte)'A';

        Assert.Throws<BackupException>(() => RegisterBackup.Open(file, Passphrase));
    }

    [Fact]
    public void ATruncatedBackupIsRefusedAsCorrupt()
    {
        var file = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);
        var split = Array.IndexOf(file, (byte)'\n');

        var truncated = file[..(split + 4)];

        Assert.Equal(
            BackupFailure.Corrupt,
            Assert.Throws<BackupException>(() => RegisterBackup.Open(truncated, Passphrase)).Failure);
    }

    /// <summary>
    /// ⭐⭐ <b>The cleartext header is authenticated.</b> It states when the backup was taken and which
    /// schema is inside, and the restore surface shows both before a passphrase is typed — so a header the
    /// file could lie about would be a lie the application repeats. Binding it in as GCM associated data
    /// is the one deliberate difference from the keystore's container.
    /// </summary>
    [Fact]
    public void EditingTheCLEARTEXTHeaderMakesTheBackupUnopenable()
    {
        var file = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);
        var text = Encoding.UTF8.GetString(file);

        var forged = text.Replace("\"schemaVersion\":2", "\"schemaVersion\":9", StringComparison.Ordinal);
        Assert.NotEqual(text, forged);

        var forgedBytes = Encoding.UTF8.GetBytes(forged);

        // The forged claim parses — that is exactly the danger…
        Assert.Equal(9, RegisterBackup.ReadHeader(forgedBytes).SchemaVersion);

        // …and it cannot be opened, so the claim can never be acted on.
        Assert.Throws<BackupException>(() => RegisterBackup.Open(forgedBytes, Passphrase));
    }

    [Fact]
    public void ATimestampEditedInTheHeaderIsAlsoRefused()
    {
        var file = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);
        var text = Encoding.UTF8.GetString(file);

        var forged = Encoding.UTF8.GetBytes(text.Replace("2026-", "2019-", StringComparison.Ordinal));

        Assert.Throws<BackupException>(() => RegisterBackup.Open(forged, Passphrase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"magic\":\"SOMETHING-ELSE\",\"version\":1}\npayload")]
    public void SomethingThatIsNotABackupIsSaidNotToBeOne(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        Assert.Equal(
            BackupFailure.NotABackup,
            Assert.Throws<BackupException>(() => RegisterBackup.Open(bytes, Passphrase)).Failure);
    }

    [Fact]
    public void AKeystoreFileIsNotMistakenForABackup()
    {
        // ⭐ The two containers look alike by design — same JSON shape, same scheme names. The magic is
        //   what keeps them apart, and an operator who picks the wrong file must be told so plainly.
        var keystore = KeyStore.Create(
            [KeyStoreEntry.Generate("R1", Stamp)], Passphrase, iterations: FastIterations);

        Assert.Equal(
            BackupFailure.NotABackup,
            Assert.Throws<BackupException>(() => RegisterBackup.Open(keystore, Passphrase)).Failure);
    }

    [Fact]
    public void ABackupFromANewerBuildIsRefusedRatherThanGuessedAt()
    {
        var file = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);
        var forged = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(file)
            .Replace("\"version\":1", "\"version\":99", StringComparison.Ordinal));

        Assert.Equal(
            BackupFailure.UnsupportedVersion,
            Assert.Throws<BackupException>(() => RegisterBackup.Open(forged, Passphrase)).Failure);
    }

    [Fact]
    public void AnUnknownSchemeOrKdfIsRefusedRatherThanAttempted()
    {
        var file = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);
        var forged = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(file)
            .Replace("\"kdf\":\"PBKDF2-SHA256\"", "\"kdf\":\"ARGON2\"", StringComparison.Ordinal));

        Assert.Equal(
            BackupFailure.UnsupportedScheme,
            Assert.Throws<BackupException>(() => RegisterBackup.Open(forged, Passphrase)).Failure);
    }

    [Fact]
    public void AnAbsurdIterationCountIsRefusedInsteadOfHonoured()
    {
        // ⚠ The count sits in a cleartext header anyone can edit. Honouring a claimed two billion
        //   iterations would hang inside the KDF with no way out — a denial of service against the
        //   operator, delivered in a file.
        var file = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);
        var forged = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(file)
            .Replace("\"iterations\":1,", "\"iterations\":2000000000,", StringComparison.Ordinal));

        Assert.Equal(
            BackupFailure.Corrupt,
            Assert.Throws<BackupException>(() => RegisterBackup.Open(forged, Passphrase)).Failure);
    }

    [Fact]
    public void TwoBackupsOfTheSameSnapshotDifferBecauseTheSaltAndNonceAreFresh()
    {
        var first = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);
        var second = RegisterBackup.Create(Snapshot, Passphrase, Stamp, 2, FastIterations);

        Assert.NotEqual(first, second);
        Assert.Equal(Snapshot, RegisterBackup.Open(first, Passphrase));
        Assert.Equal(Snapshot, RegisterBackup.Open(second, Passphrase));
    }

    /// <summary>
    /// ⭐⭐ <b>D‑1 in one assertion: the two secrets share a CONSTRUCTION, never a key.</b> The parameters
    /// are pinned equal to the keystore's so that "one reviewed set of numbers" stays a fact rather than
    /// an intention — while the salts, the passphrases and the files remain entirely separate.
    /// </summary>
    [Fact]
    public void TheBackupUsesTheSameReviewedCryptoParametersAsTheKeystore()
    {
        Assert.Equal(KeyStore.DefaultIterations, RegisterBackup.DefaultIterations);
        Assert.Equal(KeyStore.Scheme, RegisterBackup.Scheme);
        Assert.Equal(KeyStore.Kdf, RegisterBackup.Kdf);
        Assert.NotEqual(KeyStore.Magic, RegisterBackup.Magic);
    }

    /// <summary>
    /// ⛔⛔ <b>D‑1, enforced structurally.</b> Nothing on the backup or restore path may reach the
    /// keystore's passphrase, its types, or the signing session. An operator typing the same words is a
    /// choice; code sharing the secret would be a coupling nobody could see from the outside.
    /// </summary>
    [Fact]
    public void NothingOnTheBackupPathTouchesTheKeystoreOrTheSigningKey()
    {
        var suspects = new[]
        {
            typeof(RegisterBackup),
            typeof(Services.BackupWorkflow),
            typeof(Services.RestoreWorkflow),
        };

        var forbidden = new[]
        {
            typeof(KeyStore), typeof(IssuingKey), typeof(KeyStoreEntry),
            typeof(Services.SigningSession),
        };

        foreach (var suspect in suspects)
        {
            var referenced = suspect
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(TypesOf)
                .ToList();

            foreach (var banned in forbidden)
            {
                Assert.DoesNotContain(banned, referenced);
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<Type> TypesOf(MemberInfo member) => member switch
    {
        FieldInfo field => [field.FieldType],
        PropertyInfo property => [property.PropertyType],
        MethodInfo method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType),
        ConstructorInfo constructor => constructor.GetParameters().Select(p => p.ParameterType),
        _ => [],
    };

    private static DateTimeOffset Stamp => new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
}
