using System;
using System.IO;
using System.Linq;
using EmberTern.App.Security;
using EmberTern.Core.Connections;
using EmberTern.Core.Security;
using Xunit;

namespace EmberTern.Tests;

public class ConnectionProfileStoreTests
{
    // Reversible, human-readable stand-in for DPAPI: "secret" -> "ENC:secret".
    // Lets the at-rest tests assert the password is transformed without depending on
    // the platform crypto (which is exercised separately by the DPAPI round-trip).
    private static SecretProtector FakeProtector() =>
        new(s => "ENC:" + s, s => s.StartsWith("ENC:", StringComparison.Ordinal) ? s.Substring(4) : s);

    [Fact]
    public void RoundtripsProfilesThroughJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConnectionProfileStore(dir);

            var profile = new ConnectionProfile
            {
                Name = "Test",
                Host = "192.168.1.10",
                Port = 4050,
                DatabasePath = "/srv/db/test.fdb",
                Username = "SYSDBA",
                Password = "secret",
                Charset = "WIN1250",
                Dialect = 3,
                ClientLibraryPath = @"C:\Program Files\Firebird\Firebird_3_0\fbclient.dll",
            };

            store.Upsert(profile);

            var reloaded = store.LoadAll();
            Assert.Single(reloaded);
            Assert.Equal("Test", reloaded[0].Name);
            Assert.Equal(4050, reloaded[0].Port);
            Assert.Equal("WIN1250", reloaded[0].Charset);
            Assert.Equal(3, reloaded[0].Dialect);
            Assert.Equal(profile.ClientLibraryPath, reloaded[0].ClientLibraryPath);
            Assert.Equal(profile.Id, reloaded[0].Id);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DeleteRemovesOnlyMatchingProfile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConnectionProfileStore(dir);
            var a = new ConnectionProfile { Name = "A" };
            var b = new ConnectionProfile { Name = "B" };
            store.Upsert(a);
            store.Upsert(b);

            store.Delete(a.Id);

            var remaining = store.LoadAll();
            Assert.Single(remaining);
            Assert.Equal("B", remaining[0].Name);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void NewProfile_DefaultsBothLanesToReadCommitted()
    {
        var p = new ConnectionProfile();
        Assert.Equal(TransactionProfile.ReadCommitted, p.DataTransactionProfile);
        Assert.Equal(TransactionProfile.ReadCommitted, p.MetadataTransactionProfile);
    }

    [Fact]
    public void RoundtripsBothTransactionProfiles_AsStringNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConnectionProfileStore(dir);
            var profile = new ConnectionProfile
            {
                Name = "Admin",
                DatabasePath = "/srv/db/test.fdb",
                DataTransactionProfile = TransactionProfile.Snapshot,
                MetadataTransactionProfile = TransactionProfile.ReadWriteTableStability,
            };
            store.Upsert(profile);

            // Persisted as the enum NAMEs, not magic numbers (readable + reorder-safe).
            var json = File.ReadAllText(store.FilePath);
            Assert.Contains("Snapshot", json);
            Assert.Contains("ReadWriteTableStability", json);

            var reloaded = store.LoadAll();
            Assert.Single(reloaded);
            Assert.Equal(TransactionProfile.Snapshot, reloaded[0].DataTransactionProfile);
            Assert.Equal(TransactionProfile.ReadWriteTableStability, reloaded[0].MetadataTransactionProfile);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LegacyJsonWithoutTransactionProfiles_LoadsBothAsReadCommitted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            // A connections.json from before the transaction-profile field existed.
            File.WriteAllText(
                Path.Combine(dir, "connections.json"),
                "[{\"Name\":\"Legacy\",\"Host\":\"localhost\",\"Port\":3050,\"DatabasePath\":\"/db/x.fdb\"}]");

            var store = new ConnectionProfileStore(dir);
            var reloaded = store.LoadAll();
            Assert.Single(reloaded);
            Assert.Equal(TransactionProfile.ReadCommitted, reloaded[0].DataTransactionProfile);
            Assert.Equal(TransactionProfile.ReadCommitted, reloaded[0].MetadataTransactionProfile);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LegacySingleTransactionProfile_MigratesToDataLane_MetadataStaysReadCommitted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            // A pre-split connections.json carrying the single "TransactionProfile" field.
            File.WriteAllText(
                Path.Combine(dir, "connections.json"),
                "[{\"Name\":\"Legacy\",\"DatabasePath\":\"/db/x.fdb\",\"TransactionProfile\":\"ReadWriteTableStability\"}]");

            var store = new ConnectionProfileStore(dir);
            var reloaded = store.LoadAll();
            Assert.Single(reloaded);
            // Variant A: old value → Data, Metadata defaults to the safe ReadCommitted.
            Assert.Equal(TransactionProfile.ReadWriteTableStability, reloaded[0].DataTransactionProfile);
            Assert.Equal(TransactionProfile.ReadCommitted, reloaded[0].MetadataTransactionProfile);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CharsetCatalogIncludesPolishErpCharsets()
    {
        Assert.Contains("UTF8", CharsetCatalog.Supported);
        Assert.Contains("WIN1250", CharsetCatalog.Supported);
        Assert.Contains("ISO8859_1", CharsetCatalog.Supported);
    }

    [Fact]
    public void Password_IsEncryptedAtRest_AndDecryptedOnLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConnectionProfileStore(dir, FakeProtector());
            store.Upsert(new ConnectionProfile { Name = "Sec", DatabasePath = "/db/x.fdb", Password = "secret" });

            // Whole-file encryption: the on-disk content is the protector's output, not
            // raw JSON (the FakeProtector prefixes "ENC:"; a real DPAPI protector would
            // produce opaque ciphertext — so the password isn't visible in production).
            var onDisk = File.ReadAllText(store.FilePath);
            Assert.StartsWith("ENC:", onDisk);
            // The protector was applied over the JSON, so the inner schema is present
            // only after decrypting — the raw file is not plain JSON.
            Assert.False(onDisk.TrimStart().StartsWith("{", StringComparison.Ordinal));

            // In memory: round-trips back to the plaintext.
            var reloaded = store.LoadAll();
            Assert.Single(reloaded);
            Assert.Equal("secret", reloaded[0].Password);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LegacyPlaintextArray_IsMigratedToEncryptedFile_AndDeleted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            // A pre-encryption connections.json: bare array, plaintext "Password".
            var legacyPath = Path.Combine(dir, "connections.json");
            File.WriteAllText(
                legacyPath,
                "[{\"Name\":\"Legacy\",\"DatabasePath\":\"/db/x.fdb\",\"Password\":\"plain\"}]");

            var store = new ConnectionProfileStore(dir, FakeProtector());

            // Load migrates the legacy file into the unified store and returns plaintext.
            var reloaded = store.LoadAll();
            Assert.Single(reloaded);
            Assert.Equal("plain", reloaded[0].Password);

            // The legacy connections.json is deleted; the unified settings.dat replaces it
            // and is encrypted (whole-file protector applied).
            Assert.False(File.Exists(legacyPath));
            Assert.True(File.Exists(store.FilePath));
            Assert.StartsWith("ENC:", File.ReadAllText(store.FilePath));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UndecryptableFile_DegradesToEmptySettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            // Write an encrypted file with a working protector...
            new ConnectionProfileStore(dir, FakeProtector())
                .Upsert(new ConnectionProfile { Name = "Sec", DatabasePath = "/db/x.fdb", Password = "secret" });

            // ...then load it with a protector that can't decrypt (e.g. a DPAPI blob
            // from another machine/account). Whole-file encryption means the ENTIRE file
            // is unreadable — it degrades to empty rather than crashing (and is NOT
            // overwritten, so it may still decrypt on the right machine).
            var throwing = new SecretProtector(s => s, _ => throw new InvalidOperationException("cannot decrypt"));
            var reloaded = new ConnectionProfileStore(dir, throwing).LoadAll();

            Assert.Empty(reloaded);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EmptyPassword_RoundTrips_AsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConnectionProfileStore(dir, FakeProtector());
            store.Upsert(new ConnectionProfile { Name = "NoPass", DatabasePath = "/db/x.fdb", Password = "" });

            var reloaded = store.LoadAll();
            Assert.Single(reloaded);
            Assert.Equal(string.Empty, reloaded[0].Password);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DpapiSecretProtector_RoundTrips()
    {
        // DPAPI is Windows-only; skip the round-trip elsewhere.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(string.Empty, DpapiSecretProtector.Protect(string.Empty));
        Assert.Equal(string.Empty, DpapiSecretProtector.Unprotect(string.Empty));

        var encrypted = DpapiSecretProtector.Protect("hunter2");
        Assert.NotEqual("hunter2", encrypted);
        Assert.Equal("hunter2", DpapiSecretProtector.Unprotect(encrypted));
    }

    [Fact]
    public void UpsertWithSameId_UpdatesInPlace_DoesNotInsertDuplicate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConnectionProfileStore(dir);
            var original = new ConnectionProfile { Name = "Local", Host = "127.0.0.1", Port = 3050 };
            store.Upsert(original);

            var edited = new ConnectionProfile
            {
                Id = original.Id,
                Name = "Local (edited)",
                Host = "192.168.1.50",
                Port = 4050,
            };
            store.Upsert(edited);

            var all = store.LoadAll();
            Assert.Single(all);
            Assert.Equal("Local (edited)", all[0].Name);
            Assert.Equal(4050, all[0].Port);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
