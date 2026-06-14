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

    // Set when the last Load degraded instead of returning data — a file from a newer
    // build (container version, encryption scheme, or data SchemaVersion ahead of us).
    // Null after a normal load. Surfaced for diagnostics/tests; the facades don't read it.
    public string? LastLoadDiagnostic { get; private set; }

    // Set when the last Save refused to write because the file already on disk was
    // produced by a newer build (downgrade protection — see ExistingFileIsFromFuture).
    // Null after a normal save.
    public string? LastSaveDiagnostic { get; private set; }

    // Returns the persisted settings, or null when there is nothing usable to load:
    // the file is missing (and no legacy files to migrate), empty, corrupt, or can't be
    // decrypted. Callers (the section facades) treat null as "no saved state" and start
    // from defaults. A null return never overwrites whatever is on disk.
    public ApplicationSettings? Load()
    {
        LastLoadDiagnostic = null;

        if (!File.Exists(_filePath))
        {
            // First run on this build (or a fresh install). Pull in any legacy files.
            return MigrateFromLegacy();
        }

        try
        {
            var raw = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string payload;
            string scheme;
            if (SettingsFileContainer.TryParse(raw, out var header, out var parsedPayload))
            {
                // DOWNGRADE PROTECTION (container axis): a header version we don't know
                // means a newer build changed the envelope layout. Refuse to read it (and
                // Save refuses to overwrite it) so the newer file is left intact.
                if (header.ContainerVersion > SettingsFileContainer.CurrentContainerVersion)
                {
                    LastLoadDiagnostic =
                        $"settings.dat container version {header.ContainerVersion} is newer than supported " +
                        $"{SettingsFileContainer.CurrentContainerVersion}; refusing to read or overwrite " +
                        "(written by a newer EmberTern build).";
                    return null;
                }

                scheme = header.EncryptionScheme;
                payload = parsedPayload;
            }
            else
            {
                // Legacy headerless settings.dat: the whole file is the payload, encrypted
                // by whatever protector this build injects (DPAPI in production, Identity
                // in tests). It is re-wrapped with a container header on the next Save.
                scheme = _protector.Scheme;
                payload = raw;
            }

            var protector = ResolveProtector(scheme);
            if (protector is null)
            {
                // DOWNGRADE PROTECTION (encryption axis): a scheme we have no protector for
                // — typically a newer encryption algorithm. We can't decrypt it; leave it.
                LastLoadDiagnostic =
                    $"settings.dat uses encryption scheme '{scheme}' which this build cannot handle; " +
                    "refusing to read or overwrite (likely written by a newer EmberTern build).";
                return null;
            }

            var json = protector.Unprotect(payload);
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions);
            if (settings is null)
            {
                return null;
            }

            // DOWNGRADE PROTECTION (data axis): the payload decrypted fine but its data
            // SchemaVersion is from the future. We don't understand those fields and a Save
            // would silently drop them — refuse so the newer build's file stays intact.
            if (settings.SchemaVersion > CurrentSchemaVersion)
            {
                LastLoadDiagnostic =
                    $"settings.dat schema version {settings.SchemaVersion} is newer than supported " +
                    $"{CurrentSchemaVersion}; refusing to migrate or overwrite " +
                    "(written by a newer EmberTern build).";
                return null;
            }

            MigrateToCurrentVersion(settings);
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

    // Picks the protector for a stored payload's declared scheme. Today the store holds a
    // single injected protector; this is the seam where future schemes (AES, passphrase
    // export/import) get registered. Returns null for a scheme we can't handle — the
    // caller degrades safely (downgrade protection). A plaintext ("none") payload is
    // always readable regardless of the injected protector (e.g. a dev/exported file
    // opened by a DPAPI build); writing still uses the injected protector.
    private SecretProtector? ResolveProtector(string scheme)
    {
        if (string.Equals(scheme, _protector.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return _protector;
        }
        if (string.Equals(scheme, EncryptionSchemes.None, StringComparison.OrdinalIgnoreCase))
        {
            return SecretProtector.Identity;
        }
        return null;
    }

    public void Save(ApplicationSettings settings)
    {
        LastSaveDiagnostic = null;

        // DOWNGRADE PROTECTION: never clobber a settings.dat that a newer build wrote.
        // The in-memory change is dropped (the older build can't represent the newer
        // data anyway); the newer file survives so the user loses nothing on next launch
        // of the newer build.
        if (ExistingFileIsFromFuture(out var diagnostic))
        {
            LastSaveDiagnostic = diagnostic;
            return;
        }

        settings.SchemaVersion = CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var payload = _protector.Protect(json);
        var container = SettingsFileContainer.Wrap(
            SettingsFileContainer.CurrentContainerVersion, _protector.Scheme, payload);
        AtomicWrite(_filePath, container);
    }

    // True only when the file already on disk was written by a NEWER build than this one
    // (newer container layout, an encryption scheme we can't read, or a newer data
    // SchemaVersion). Corrupt / undecryptable-but-known-scheme files are NOT treated as
    // future — they are safe to replace, matching the prior overwrite behaviour (we never
    // want to strand the user forever on a genuinely broken file).
    private bool ExistingFileIsFromFuture(out string diagnostic)
    {
        diagnostic = string.Empty;
        if (!File.Exists(_filePath))
        {
            return false;
        }

        string raw;
        try
        {
            raw = File.ReadAllText(_filePath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string payload;
        string scheme;
        if (SettingsFileContainer.TryParse(raw, out var header, out var parsedPayload))
        {
            if (header.ContainerVersion > SettingsFileContainer.CurrentContainerVersion)
            {
                diagnostic = $"Refusing to overwrite settings.dat: container version {header.ContainerVersion} " +
                             $"is newer than supported {SettingsFileContainer.CurrentContainerVersion}.";
                return true;
            }
            scheme = header.EncryptionScheme;
            payload = parsedPayload;
        }
        else
        {
            scheme = _protector.Scheme;
            payload = raw;
        }

        var protector = ResolveProtector(scheme);
        if (protector is null)
        {
            diagnostic = $"Refusing to overwrite settings.dat: unknown encryption scheme '{scheme}' " +
                         "(written by a newer EmberTern build).";
            return true;
        }

        try
        {
            var json = protector.Unprotect(payload);
            var existing = JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions);
            if (existing is not null && existing.SchemaVersion > CurrentSchemaVersion)
            {
                diagnostic = $"Refusing to overwrite settings.dat: schema version {existing.SchemaVersion} " +
                             $"is newer than supported {CurrentSchemaVersion}.";
                return true;
            }
        }
        catch (Exception)
        {
            // Corrupt / undecryptable with a known scheme → not a future file; allow the
            // replace (consistent with prior behaviour; never strand on a broken file).
            return false;
        }

        return false;
    }

    // Brings a freshly-loaded ApplicationSettings up to CurrentSchemaVersion. Two layers:
    //
    //  1. A defensive, version-independent consume of the v1→v2 transaction-profile shim
    //     (idempotent — no-op when the shim field is absent). Kept so a stray legacy field
    //     can never leak back into a saved file regardless of the recorded version.
    //
    //  2. A stepwise migration ladder. Each step upgrades by exactly ONE version and is
    //     independent of the others, so a future contributor adds:
    //
    //         case 2:
    //             Migrate_2_3(settings);
    //             break;
    //
    //     without needing to understand any earlier step. Files from the future are
    //     already rejected in Load (downgrade protection), so here we always have
    //     SchemaVersion <= CurrentSchemaVersion.
    private void MigrateToCurrentVersion(ApplicationSettings settings)
    {
        Migrate_1_2(settings);

        while (settings.SchemaVersion < CurrentSchemaVersion)
        {
            switch (settings.SchemaVersion)
            {
                case 1:
                    // 1 → 2: split the single TransactionProfile into Data/Metadata lanes.
                    // The data fix-up is the shim consumed by Migrate_1_2 above; this step
                    // only advances the version stamp.
                    break;

                // Future steps go here, one per version. Template:
                // case 2:
                //     Migrate_2_3(settings);
                //     break;

                default:
                    // No registered step for this version. Stop rather than loop forever;
                    // Save stamps CurrentSchemaVersion onto the (current-shaped) data.
                    settings.SchemaVersion = CurrentSchemaVersion;
                    return;
            }

            settings.SchemaVersion++;
        }
    }

    // v1 → v2: the pre-split single ConnectionProfile.TransactionProfile (carried on the
    // read-only LegacyTransactionProfile shim) maps onto the Data/Metadata pair — variant
    // A: Data inherits the old value, Metadata defaults to the safe ReadCommitted so a
    // metadata-only profile change can't leak into everyday data work. Idempotent: after
    // the next Save the legacy field is gone, so subsequent loads no-op.
    private static void Migrate_1_2(ApplicationSettings settings)
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
