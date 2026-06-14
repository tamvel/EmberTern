using System;
using System.IO;
using EmberTern.Core.Connections;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;
using Xunit;

namespace EmberTern.Tests;

// Covers the settings.dat file container (KROK 1), explicit encryption-scheme tagging
// (KROK 2), downgrade protection (KROK 3), and the stepwise migration ladder (KROK 4).
public class SettingsContainerTests
{
    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));

    // ---- Pure container parser ---------------------------------------------------

    [Fact]
    public void Container_WrapThenParse_RoundTrips()
    {
        var wrapped = SettingsFileContainer.Wrap(1, EncryptionSchemes.Dpapi, "PAYLOAD-BYTES");

        Assert.True(SettingsFileContainer.TryParse(wrapped, out var header, out var payload));
        Assert.Equal(1, header.ContainerVersion);
        Assert.Equal(EncryptionSchemes.Dpapi, header.EncryptionScheme);
        Assert.Equal("PAYLOAD-BYTES", payload);
    }

    [Fact]
    public void Container_TryParse_ReturnsFalse_ForLegacyHeaderlessBlob()
    {
        // A legacy DPAPI settings.dat is a single Base64 line — no magic, no tabs.
        Assert.False(SettingsFileContainer.TryParse("QUJDREVGMTIzNDU2Nzg5MA==", out _, out var payload));
        Assert.Equal("QUJDREVGMTIzNDU2Nzg5MA==", payload);
    }

    [Fact]
    public void Container_TryParse_ReturnsFalse_ForLegacyPlaintextJson()
    {
        // A legacy Identity settings.dat is raw indented JSON — first line is "{".
        const string json = "{\n  \"SchemaVersion\": 1\n}";
        Assert.False(SettingsFileContainer.TryParse(json, out _, out var payload));
        Assert.Equal(json, payload);
    }

    [Fact]
    public void Container_TryParse_ToleratesExtraTrailingHeaderFields()
    {
        // Forward-compat: a future header may append fields; the current parser ignores them.
        var wrapped = $"{SettingsFileContainer.Magic}\t1\t{EncryptionSchemes.None}\tRESERVED\tMORE\nbody";
        Assert.True(SettingsFileContainer.TryParse(wrapped, out var header, out var payload));
        Assert.Equal(1, header.ContainerVersion);
        Assert.Equal(EncryptionSchemes.None, header.EncryptionScheme);
        Assert.Equal("body", payload);
    }

    // ---- Store writes/reads the container ---------------------------------------

    [Fact]
    public void Save_WritesContainerHeader_WithMagicVersionAndScheme()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ApplicationSettingsStore(dir); // Identity → scheme "none"
            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "A" } } });

            var raw = File.ReadAllText(store.FilePath);
            Assert.True(SettingsFileContainer.TryParse(raw, out var header, out var payload));
            Assert.Equal(SettingsFileContainer.CurrentContainerVersion, header.ContainerVersion);
            Assert.Equal(EncryptionSchemes.None, header.EncryptionScheme);
            // Identity payload is the raw JSON.
            Assert.StartsWith("{", payload.TrimStart());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsLegacyHeaderlessFile_ThenSave_AddsContainer()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var store = new ApplicationSettingsStore(dir); // Identity
            // A pre-container settings.dat: raw JSON, no header.
            File.WriteAllText(store.FilePath,
                "{\"SchemaVersion\":2,\"Connections\":[{\"Name\":\"Legacy\"}]}");

            // Read it back untouched (backward compatibility).
            var loaded = store.Load();
            Assert.NotNull(loaded);
            Assert.Equal("Legacy", loaded!.Connections[0].Name);
            Assert.Null(store.LastLoadDiagnostic);

            // First save re-wraps it in the new container.
            store.Save(loaded);
            Assert.True(SettingsFileContainer.TryParse(File.ReadAllText(store.FilePath), out var header, out _));
            Assert.Equal(SettingsFileContainer.CurrentContainerVersion, header.ContainerVersion);
            Assert.Equal(EncryptionSchemes.None, header.EncryptionScheme);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ---- KROK 3: downgrade protection -------------------------------------------

    [Fact]
    public void Load_ReturnsNull_AndDiagnostic_WhenContainerVersionFromFuture()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var store = new ApplicationSettingsStore(dir);
            File.WriteAllText(store.FilePath,
                SettingsFileContainer.Wrap(999, EncryptionSchemes.None, "{\"SchemaVersion\":2}"));

            Assert.Null(store.Load());
            Assert.NotNull(store.LastLoadDiagnostic);
            Assert.Contains("container version 999", store.LastLoadDiagnostic!);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsNull_AndDiagnostic_WhenEncryptionSchemeUnknown()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var store = new ApplicationSettingsStore(dir);
            File.WriteAllText(store.FilePath,
                SettingsFileContainer.Wrap(1, "aes256-future", "opaque-ciphertext"));

            Assert.Null(store.Load());
            Assert.NotNull(store.LastLoadDiagnostic);
            Assert.Contains("aes256-future", store.LastLoadDiagnostic!);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsNull_AndDiagnostic_WhenDataSchemaVersionFromFuture()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var store = new ApplicationSettingsStore(dir);
            File.WriteAllText(store.FilePath,
                SettingsFileContainer.Wrap(1, EncryptionSchemes.None, "{\"SchemaVersion\":999,\"Connections\":[]}"));

            Assert.Null(store.Load());
            Assert.NotNull(store.LastLoadDiagnostic);
            Assert.Contains("schema version 999", store.LastLoadDiagnostic!);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_RefusesToOverwrite_FutureContainerVersion()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var store = new ApplicationSettingsStore(dir);
            var future = SettingsFileContainer.Wrap(999, EncryptionSchemes.None, "{\"SchemaVersion\":2}");
            File.WriteAllText(store.FilePath, future);

            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "New" } } });

            // The newer file is left intact; the in-memory change was not persisted.
            Assert.Equal(future, File.ReadAllText(store.FilePath));
            Assert.NotNull(store.LastSaveDiagnostic);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_RefusesToOverwrite_FutureDataSchemaVersion()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var store = new ApplicationSettingsStore(dir);
            var future = SettingsFileContainer.Wrap(1, EncryptionSchemes.None, "{\"SchemaVersion\":999,\"Connections\":[]}");
            File.WriteAllText(store.FilePath, future);

            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "New" } } });

            Assert.Equal(future, File.ReadAllText(store.FilePath));
            Assert.NotNull(store.LastSaveDiagnostic);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ---- KROK 4: migration ladder ------------------------------------------------

    [Fact]
    public void MigrationLadder_StampsCurrentSchemaVersion_OnLegacyV1File()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var store = new ApplicationSettingsStore(dir);
            // Headerless v1 settings.dat carrying the pre-split single transaction profile.
            File.WriteAllText(store.FilePath,
                "{\"SchemaVersion\":1,\"Connections\":[{\"Name\":\"Old\",\"TransactionProfile\":\"Snapshot\"}]}");

            var loaded = store.Load();
            Assert.NotNull(loaded);
            // Ladder ran 1 → 2: the version stamp advanced and the shim was consumed.
            Assert.Equal(ApplicationSettingsStore.CurrentSchemaVersion, loaded!.SchemaVersion);
            Assert.Equal(TransactionProfile.Snapshot, loaded.Connections[0].DataTransactionProfile);
            Assert.Equal(TransactionProfile.ReadCommitted, loaded.Connections[0].MetadataTransactionProfile);
            Assert.Null(loaded.Connections[0].LegacyTransactionProfile);

            // Re-saved as a current container; reload is a clean no-op migration.
            store.Save(loaded);
            var reloaded = store.Load();
            Assert.NotNull(reloaded);
            Assert.Equal(ApplicationSettingsStore.CurrentSchemaVersion, reloaded!.SchemaVersion);
            Assert.Null(store.LastLoadDiagnostic);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
