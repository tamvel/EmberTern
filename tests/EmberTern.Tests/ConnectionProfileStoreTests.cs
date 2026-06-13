using System.IO;
using System.Linq;
using EmberTern.Core.Connections;
using Xunit;

namespace EmberTern.Tests;

public class ConnectionProfileStoreTests
{
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
    public void NewProfile_DefaultsToReadCommitted()
    {
        Assert.Equal(TransactionProfile.ReadCommitted, new ConnectionProfile().TransactionProfile);
    }

    [Fact]
    public void RoundtripsTransactionProfile_AsStringName()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConnectionProfileStore(dir);
            var profile = new ConnectionProfile
            {
                Name = "Admin",
                DatabasePath = "/srv/db/test.fdb",
                TransactionProfile = TransactionProfile.ReadWriteTableStability,
            };
            store.Upsert(profile);

            // Persisted as the enum NAME, not a magic number (readable + reorder-safe).
            var json = File.ReadAllText(store.FilePath);
            Assert.Contains("ReadWriteTableStability", json);

            var reloaded = store.LoadAll();
            Assert.Single(reloaded);
            Assert.Equal(TransactionProfile.ReadWriteTableStability, reloaded[0].TransactionProfile);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LegacyJsonWithoutTransactionProfile_LoadsAsReadCommitted()
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
            Assert.Equal(TransactionProfile.ReadCommitted, reloaded[0].TransactionProfile);
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
