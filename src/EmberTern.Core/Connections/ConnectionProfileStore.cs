using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmberTern.Core.Security;

namespace EmberTern.Core.Connections;

public sealed class ConnectionProfileStore
{
    // Bump when the on-disk shape changes in a way older readers must notice.
    // v0 = legacy bare JSON array with plaintext "Password" (pre-encryption).
    // v1 = container object { SchemaVersion, Connections[] } with encrypted
    //      "ProtectedPassword" and no plaintext password field.
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // TransactionProfile (and any future enum) serialized as its name, not a
        // magic number — readable in connections.json and stable across reorders.
        Converters = { new JsonStringEnumConverter() },
        // Keep the v1 file clean: the legacy plaintext "Password" DTO field is null
        // on save and must not be written back out.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly SecretProtector _protector;

    public ConnectionProfileStore()
        : this(DefaultStoreDirectory(), null)
    {
    }

    public ConnectionProfileStore(SecretProtector protector)
        : this(DefaultStoreDirectory(), protector)
    {
    }

    public ConnectionProfileStore(string directory)
        : this(directory, null)
    {
    }

    public ConnectionProfileStore(string directory, SecretProtector? protector)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "connections.json");
        // No protector injected (tests, or a caller that doesn't care) → Identity,
        // i.e. passwords stored verbatim. Production always passes the DPAPI one.
        _protector = protector ?? SecretProtector.Identity;
    }

    public string FilePath => _filePath;

    public IReadOnlyList<ConnectionProfile> LoadAll()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<ConnectionProfile>();
        }

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ConnectionProfile>();
        }

        var (profiles, migratedFromLegacy) = Deserialize(json);

        // Auto-migration: a legacy plaintext file (root JSON array, cleartext
        // passwords) is rewritten once into the encrypted v1 container. A deliberate
        // one-time write triggered by a read — it secures existing installs on the
        // first launch after the upgrade, with no user action. Write failures are
        // swallowed: the loaded profiles are still returned (app works), and the next
        // launch retries the migration.
        if (migratedFromLegacy)
        {
            try
            {
                SaveAll(profiles);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return profiles;
    }

    public void SaveAll(IEnumerable<ConnectionProfile> profiles)
    {
        var file = new ConnectionsFile
        {
            SchemaVersion = CurrentSchemaVersion,
            Connections = profiles.Select(ToDto).ToList(),
        };
        var json = JsonSerializer.Serialize(file, JsonOptions);
        AtomicWrite(_filePath, json);
    }

    public void Upsert(ConnectionProfile profile)
    {
        var profiles = LoadAll().ToList();
        var existing = profiles.FindIndex(p => p.Id == profile.Id);
        if (existing >= 0)
        {
            profiles[existing] = profile;
        }
        else
        {
            profiles.Add(profile);
        }
        SaveAll(profiles);
    }

    public void Delete(string id)
    {
        var profiles = LoadAll().Where(p => p.Id != id);
        SaveAll(profiles);
    }

    // Returns the loaded profiles and whether the source file was the legacy
    // plaintext format (so the caller can trigger a one-time re-save).
    private (List<ConnectionProfile> Profiles, bool MigratedFromLegacy) Deserialize(string json)
    {
        // JsonException propagates (corrupt file) — same strict contract as before.
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            // v0 legacy: bare array of profiles with plaintext "Password".
            var legacy = JsonSerializer.Deserialize<List<ConnectionProfileDto>>(json, JsonOptions)
                         ?? new List<ConnectionProfileDto>();
            return (legacy.Select(FromLegacyDto).ToList(), MigratedFromLegacy: true);
        }

        // v1+: container object.
        var file = JsonSerializer.Deserialize<ConnectionsFile>(json, JsonOptions);
        var connections = file?.Connections ?? new List<ConnectionProfileDto>();
        return (connections.Select(FromDto).ToList(), MigratedFromLegacy: false);
    }

    private ConnectionProfile FromDto(ConnectionProfileDto d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Host = d.Host,
        Port = d.Port,
        DatabasePath = d.DatabasePath,
        Username = d.Username,
        Password = UnprotectSafe(d.ProtectedPassword),
        Charset = d.Charset,
        Dialect = d.Dialect,
        ClientLibraryPath = d.ClientLibraryPath,
        TransactionProfile = d.TransactionProfile,
    };

    private static ConnectionProfile FromLegacyDto(ConnectionProfileDto d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Host = d.Host,
        Port = d.Port,
        DatabasePath = d.DatabasePath,
        Username = d.Username,
        // Legacy files carried the password in cleartext under "Password".
        Password = d.Password ?? string.Empty,
        Charset = d.Charset,
        Dialect = d.Dialect,
        ClientLibraryPath = d.ClientLibraryPath,
        TransactionProfile = d.TransactionProfile,
    };

    private ConnectionProfileDto ToDto(ConnectionProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Host = p.Host,
        Port = p.Port,
        DatabasePath = p.DatabasePath,
        Username = p.Username,
        // Never persist the plaintext password. Only the protected form is written.
        Password = null,
        ProtectedPassword = string.IsNullOrEmpty(p.Password) ? string.Empty : _protector.Protect(p.Password),
        Charset = p.Charset,
        Dialect = p.Dialect,
        ClientLibraryPath = p.ClientLibraryPath,
        TransactionProfile = p.TransactionProfile,
    };

    // Decrypt defensively: a DPAPI blob from another Windows account/machine, or a
    // corrupt Base64 value, can't be decrypted here. Rather than crash the load, the
    // password degrades to empty and the user re-enters it (the connection attempt
    // then surfaces the server's own auth error).
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

    // Write via a temp file in the same directory, then atomically swap it in. Avoids
    // a torn connections.json if the process dies mid-write (the old file stays intact
    // until the replace succeeds).
    private static void AtomicWrite(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        var tempPath = Path.Combine(directory, Path.GetFileName(path) + ".tmp");

        File.WriteAllText(tempPath, content);

        if (File.Exists(path))
        {
            // ReplaceFile on Windows / atomic rename on the same volume.
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

    // On-disk container (v1+). Versioned so future schema changes are recognisable.
    private sealed class ConnectionsFile
    {
        public int SchemaVersion { get; set; }
        public List<ConnectionProfileDto> Connections { get; set; } = new();
    }

    // On-disk shape of one profile. Distinct from the runtime ConnectionProfile so
    // the persistence schema can evolve independently (same split as the workspace
    // DTOs). Carries BOTH the legacy plaintext "Password" (read-only migration path,
    // never written) and the v1 "ProtectedPassword".
    private sealed class ConnectionProfileDto
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 3050;
        public string DatabasePath { get; set; } = string.Empty;
        public string Username { get; set; } = "SYSDBA";
        public string? Password { get; set; }
        public string ProtectedPassword { get; set; } = string.Empty;
        public string Charset { get; set; } = "WIN1250";
        public int Dialect { get; set; } = 3;
        public string ClientLibraryPath { get; set; } = string.Empty;
        public TransactionProfile TransactionProfile { get; set; } = TransactionProfile.ReadCommitted;
    }
}
