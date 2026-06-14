using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmberTern.Core.Connections;
using EmberTern.Core.Security;
using EmberTern.Core.Workspace;

namespace EmberTern.Core.Settings;

// The single, real persistence behind the three section facades
// (ConnectionProfileStore / FolderStore / WorkspaceStore). Serializes the whole
// ApplicationSettings aggregate to JSON, runs it through an injected SecretProtector
// (DPAPI in production), and writes one file: settings.dat.
//
// Whole-file encryption is the deliberate design: nothing inside (passwords, SQL,
// folder layout) is readable without the protector. The flip side — a settings.dat
// that can't be decrypted (e.g. copied to another Windows account/machine) loses ALL
// settings, not just passwords. Load degrades to null in that case and never
// overwrites the unreadable file (it may decrypt fine on the right machine).
public sealed class ApplicationSettingsStore
{
    // Bump when the aggregate shape changes in a way older readers must notice.
    // v1 = initial unified container (connections + folders + workspace + user settings).
    // v2 = ConnectionProfile.TransactionProfile split into Data/Metadata profiles.
    public const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Enums (TransactionProfile, WorkspaceTabKind, MetadataObjectKind, …) as their
        // names — readable in the inner JSON and stable across enum reorders.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly SecretProtector _protector;

    public ApplicationSettingsStore()
        : this(DefaultStoreDirectory(), null)
    {
    }

    public ApplicationSettingsStore(SecretProtector protector)
        : this(DefaultStoreDirectory(), protector)
    {
    }

    public ApplicationSettingsStore(string directory)
        : this(directory, null)
    {
    }

    public ApplicationSettingsStore(string directory, SecretProtector? protector)
    {
        System.IO.Directory.CreateDirectory(directory);
        Directory = directory;
        _filePath = Path.Combine(directory, "settings.dat");
        // No protector injected (tests, or a caller that doesn't care) → Identity,
        // i.e. the file is written as readable JSON. Production wires DPAPI.
        _protector = protector ?? SecretProtector.Identity;
    }

    public string FilePath => _filePath;

    internal string Directory { get; }

    internal SecretProtector Protector => _protector;

    // Returns the persisted settings, or null when there is nothing usable to load:
    // the file is missing (and no legacy files to migrate), empty, corrupt, or can't be
    // decrypted. Callers (the section facades) treat null as "no saved state" and start
    // from defaults. A null return never overwrites whatever is on disk.
    public ApplicationSettings? Load()
    {
        if (!File.Exists(_filePath))
        {
            // First run on this build (or a fresh install). Pull in any legacy files.
            return MigrateFromLegacy();
        }

        try
        {
            var stored = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(stored))
            {
                return null;
            }

            var json = _protector.Unprotect(stored);
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions);
            if (settings is not null)
            {
                MigrateTransactionProfiles(settings);
            }
            return settings;
        }
        // Corrupt JSON, partial write, locked file, or an undecryptable blob (DPAPI from
        // another account/machine). Degrade to "no saved state" rather than crash — and
        // crucially, do not save over the file: it may be valid on the right machine.
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (Exception)
        {
            // SecretProtector.Unprotect can throw arbitrary crypto/format exceptions.
            return null;
        }
    }

    public void Save(ApplicationSettings settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var stored = _protector.Protect(json);
        AtomicWrite(_filePath, stored);
    }

    // Maps the pre-split single TransactionProfile (carried on the read-only
    // LegacyTransactionProfile shim) onto the Data/Metadata pair — variant A:
    // Data inherits the old value, Metadata defaults to the safe ReadCommitted so a
    // metadata-only profile change can't leak into everyday data work. Idempotent:
    // after the next Save the legacy field is gone, so subsequent loads no-op.
    private static void MigrateTransactionProfiles(ApplicationSettings settings)
    {
        foreach (var connection in settings.Connections)
        {
            if (connection.LegacyTransactionProfile is { } legacy)
            {
                connection.DataTransactionProfile = legacy;
                connection.MetadataTransactionProfile = TransactionProfile.ReadCommitted;
                connection.LegacyTransactionProfile = null;
            }
        }
    }

    // ---- Migration from the three legacy files ----------------------------------

    private static readonly string[] LegacyFileNames =
    {
        "connections.json", "folders.json", "workspace.json",
    };

    // Builds an ApplicationSettings from whatever legacy files exist, writes the unified
    // (encrypted) settings.dat, and deletes the legacy files. Returns null when there is
    // nothing to migrate (clean install). If the save fails, the legacy files are kept
    // and the in-memory result is returned so the session works and the next launch
    // retries the migration.
    private ApplicationSettings? MigrateFromLegacy()
    {
        var connectionsPath = Path.Combine(Directory, "connections.json");
        var foldersPath = Path.Combine(Directory, "folders.json");
        var workspacePath = Path.Combine(Directory, "workspace.json");

        var anyLegacy = File.Exists(connectionsPath)
                        || File.Exists(foldersPath)
                        || File.Exists(workspacePath);
        if (!anyLegacy)
        {
            return null;
        }

        var settings = new ApplicationSettings
        {
            Connections = ReadLegacyConnections(connectionsPath),
            Folders = ReadLegacyJson<FolderState>(foldersPath) ?? new FolderState(),
            Workspace = ReadLegacyJson<WorkspaceState>(workspacePath) ?? new WorkspaceState(),
        };

        try
        {
            Save(settings);
        }
        catch (IOException)
        {
            return settings; // Keep legacy files; retry next launch.
        }
        catch (UnauthorizedAccessException)
        {
            return settings;
        }

        DeleteLegacyFiles();
        return settings;
    }

    private void DeleteLegacyFiles()
    {
        foreach (var name in LegacyFileNames)
        {
            try
            {
                var path = Path.Combine(Directory, name);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort: a leftover legacy file is harmless (settings.dat now wins).
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private T? ReadLegacyJson<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Reads the legacy connections.json in either shape:
    //   v0 — a bare JSON array with plaintext "Password".
    //   v1 — { SchemaVersion, Connections[] } with "ProtectedPassword" (DPAPI Base64),
    //        recovered via the same protector and degraded to empty on failure.
    private List<ConnectionProfile> ReadLegacyConnections(string path)
    {
        if (!File.Exists(path))
        {
            return new List<ConnectionProfile>();
        }

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<ConnectionProfile>();
            }

            using var doc = JsonDocument.Parse(json);
            List<LegacyConnectionDto> dtos;
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                dtos = JsonSerializer.Deserialize<List<LegacyConnectionDto>>(json, JsonOptions)
                       ?? new List<LegacyConnectionDto>();
            }
            else
            {
                var file = JsonSerializer.Deserialize<LegacyConnectionsFile>(json, JsonOptions);
                dtos = file?.Connections ?? new List<LegacyConnectionDto>();
            }

            return dtos.Select(FromLegacyDto).ToList();
        }
        catch (JsonException)
        {
            return new List<ConnectionProfile>();
        }
        catch (IOException)
        {
            return new List<ConnectionProfile>();
        }
        catch (UnauthorizedAccessException)
        {
            return new List<ConnectionProfile>();
        }
    }

    private ConnectionProfile FromLegacyDto(LegacyConnectionDto d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Host = d.Host,
        Port = d.Port,
        DatabasePath = d.DatabasePath,
        Username = d.Username,
        // v0 carried plaintext "Password"; v1 carried encrypted "ProtectedPassword".
        // Whichever is present wins; the v1 form is decrypted defensively.
        Password = !string.IsNullOrEmpty(d.Password)
            ? d.Password
            : UnprotectSafe(d.ProtectedPassword),
        Charset = d.Charset,
        Dialect = d.Dialect,
        ClientLibraryPath = d.ClientLibraryPath,
        // Variant A: the pre-split single profile maps to Data; Metadata defaults safe.
        DataTransactionProfile = d.TransactionProfile,
        MetadataTransactionProfile = TransactionProfile.ReadCommitted,
    };

    private string UnprotectSafe(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return string.Empty;
        }
        try
        {
            return _protector.Unprotect(stored);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    // ---- I/O helpers -------------------------------------------------------------

    // Write via a temp file in the same directory, then atomically swap it in. Avoids a
    // torn settings.dat if the process dies mid-write (the old file stays intact until
    // the replace succeeds).
    private static void AtomicWrite(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        var tempPath = Path.Combine(directory, Path.GetFileName(path) + ".tmp");

        File.WriteAllText(tempPath, content);

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static string DefaultStoreDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "EmberTern");
    }

    // ---- Legacy connections.json DTOs (migration only) ---------------------------

    private sealed class LegacyConnectionsFile
    {
        public int SchemaVersion { get; set; }
        public List<LegacyConnectionDto> Connections { get; set; } = new();
    }

    private sealed class LegacyConnectionDto
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 3050;
        public string DatabasePath { get; set; } = string.Empty;
        public string Username { get; set; } = "SYSDBA";
        public string? Password { get; set; }
        public string? ProtectedPassword { get; set; }
        public string Charset { get; set; } = "WIN1250";
        public int Dialect { get; set; } = 3;
        public string ClientLibraryPath { get; set; } = string.Empty;
        public TransactionProfile TransactionProfile { get; set; } = TransactionProfile.ReadCommitted;
    }
}
