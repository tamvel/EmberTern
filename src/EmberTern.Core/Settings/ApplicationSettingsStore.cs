using System;
using System.Collections.Generic;
using System.Globalization;
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
// that can't be decrypted (e.g. copied to another Windows account/machine) makes ALL
// settings unreadable, not just the passwords.
//
// ⚠ THAT FLIP SIDE WAS A DATA-LOSS BUG until 2026-07-27 (audit A-03), and this comment
// used to assert the opposite: that Load "never overwrites the unreadable file". Load
// indeed never wrote — but SAVE did. Load returned null for an unreadable file exactly
// as it does for a fresh install, every section facade answers null with
// `?? new ApplicationSettings()` and then saves, and ExistingFileIsFromFuture
// deliberately allowed replacing a file it could not decrypt. So one grid-column resize
// destroyed the connection profiles, passwords, saved queries, workspace and watches.
//
// The fix is a distinction, not a workaround: SettingsLoadStatus separates "there is
// nothing here" from "there is something here I cannot read", and Save REFUSES in the
// second case. See LoadWithStatus and ExistingFileBlocksSave.
public sealed class ApplicationSettingsStore
{
    // Bump when the aggregate shape changes in a way older readers must notice.
    // v1 = initial unified container (connections + folders + workspace + user settings).
    // v2 = ConnectionProfile.TransactionProfile split into Data/Metadata profiles.
    public const int CurrentSchemaVersion = 2;

    // ⭐ internal, not private, because the SETTINGS EXPORT serializes the same ApplicationSettings and must do
    // it identically. If the export built its own options, the enums below would be written as numbers there and
    // as names here — two representations of one aggregate, free to drift, with the divergence invisible until a
    // file crosses between them. One aggregate, one serialization contract.
    internal static readonly JsonSerializerOptions JsonOptions = new()
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

    /// <summary>
    /// Returns the persisted settings, or null when there is nothing usable to load. Retained as the
    /// convenience every section facade uses — it deliberately keeps the old signature so none of them had to
    /// change.
    /// <para><b>Callers that decide whether to WRITE must not use this.</b> Null here still conflates "nothing
    /// saved yet" with "there is a file I cannot read", and acting on that conflation is audit A-03. What makes
    /// the facades safe is not this method but <see cref="Save"/>, which now refuses over an unreadable file.
    /// Anything that needs to tell the two apart asks <see cref="LoadWithStatus"/>.</para>
    /// </summary>
    public ApplicationSettings? Load() => LoadWithStatus().Settings;

    /// <summary>
    /// Reads <c>settings.dat</c> and reports WHAT HAPPENED, not merely whether it worked — see
    /// <see cref="SettingsLoadStatus"/> for why that distinction is a data-safety feature rather than a
    /// diagnostic nicety.
    /// <para>Never writes, except on the one path that is safe by construction: a missing file with legacy
    /// files beside it, which is a migration onto empty ground.</para>
    /// </summary>
    public SettingsLoadResult LoadWithStatus()
    {
        LastLoadDiagnostic = null;

        if (!File.Exists(_filePath))
        {
            // First run on this build (or a fresh install). Pull in any legacy files.
            var migrated = MigrateFromLegacy();
            return migrated is null ? SettingsLoadResult.Missing() : SettingsLoadResult.Loaded(migrated);
        }

        try
        {
            var raw = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(raw))
            {
                // An empty file holds no user data, so replacing it destroys nothing — Missing, not Corrupt.
                // (A zero-length settings.dat is what a disk-full or killed-mid-write leaves behind.)
                return SettingsLoadResult.Missing();
            }

            // A settings EXPORT put where settings.dat belongs. It was always refused — its magic is not ours, so
            // it falls through to the legacy-headerless path and fails to decrypt — but it was refused with the
            // DPAPI story ("written by a different Windows account"), which is untrue and unhelpful. Identity is
            // decided here, so the truthful answer belongs here too. Still Unreadable: intact data this build
            // cannot interpret in this position, and emphatically not safe to overwrite.
            if (LooksLikeASettingsExport(raw))
            {
                LastLoadDiagnostic = ExportInPlaceOfSettingsDiagnostic;
                return SettingsLoadResult.Unreadable(LastLoadDiagnostic);
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
                    return SettingsLoadResult.Future(LastLoadDiagnostic);
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
                return SettingsLoadResult.Future(LastLoadDiagnostic);
            }

            var json = protector.Unprotect(payload);
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions);
            if (settings is null)
            {
                // Valid JSON that deserialized to nothing — a literal "null" payload. The file holds no
                // settings, but it also is not what this build writes, so it is not safe to assume it is junk.
                LastLoadDiagnostic = "settings.dat decrypted but contained no settings.";
                return SettingsLoadResult.Corrupt(LastLoadDiagnostic);
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
                return SettingsLoadResult.Future(LastLoadDiagnostic);
            }

            MigrateToCurrentVersion(settings);
            return SettingsLoadResult.Loaded(settings);
        }
        // Every failure below leaves a file on disk that this build could not interpret. Each degrades to "no
        // settings in memory" rather than crashing — but NONE of them is permission to write: Save consults
        // ExistingFileBlocksSave, which reaches the same conclusion from the same file.
        //
        // The classification is deliberately by CAUSE, because the causes have different prognoses. Damaged
        // content is unlikely to fix itself; an undecryptable blob very often decrypts perfectly on the machine
        // that wrote it, so replacing it destroys recoverable data.
        catch (JsonException ex)
        {
            LastLoadDiagnostic = $"settings.dat could not be parsed: {ex.Message}";
            return SettingsLoadResult.Corrupt(LastLoadDiagnostic);
        }
        catch (IOException ex)
        {
            // Locked or unreadable right now (another process, a network path). Emphatically not junk.
            LastLoadDiagnostic = $"settings.dat could not be read: {ex.Message}";
            return SettingsLoadResult.Unreadable(LastLoadDiagnostic);
        }
        catch (UnauthorizedAccessException ex)
        {
            LastLoadDiagnostic = $"settings.dat could not be read: {ex.Message}";
            return SettingsLoadResult.Unreadable(LastLoadDiagnostic);
        }
        catch (Exception ex)
        {
            // SecretProtector.Unprotect throws arbitrary crypto/format exceptions. This is the DPAPI case —
            // the one that motivated the whole distinction, and the one where the file is most likely intact.
            LastLoadDiagnostic =
                $"settings.dat could not be decrypted: {ex.Message}. This usually means the file was written " +
                "by a different Windows account or on a different machine; it is intact and will decrypt there.";
            return SettingsLoadResult.Unreadable(LastLoadDiagnostic);
        }
    }

    // Picks the protector for a stored settings.dat payload's declared scheme. Today the store
    // holds a single injected protector; this is the seam where a future AT-REST scheme (an AES
    // machine key, say) gets registered. Returns null for a scheme we can't handle — the caller
    // degrades safely (downgrade protection). A plaintext ("none") payload is always readable
    // regardless of the injected protector (e.g. a dev file opened by a DPAPI build); writing still
    // uses the injected protector.
    //
    // ⚠ This comment used to name "passphrase export/import" among the schemes to register here,
    // and EncryptionSchemes carried the matching instruction. Both were written before the export
    // had its own envelope, and neither survives the design — see the note on
    // EncryptionSchemes.PassphraseAes256. The short version: this method has no passphrase, so
    // registering that scheme could only return a protector that cannot decrypt, which would turn
    // an honest refusal into a misleading "could not be decrypted".
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
        // Explicit, so the decision is visible where someone would go to make it rather than being
        // a fall-through. A passphrase-encrypted payload belongs to a settings EXPORT, is opened by
        // SettingsImportReader with a protector built from that file's own header, and is never a
        // settings.dat payload.
        if (string.Equals(scheme, EncryptionSchemes.PassphraseAes256, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return null;
    }

    // The export's magic, byte-compared against the front of the file. Deliberately a plain prefix test on text
    // we have already read: this is a diagnosis of a file we are refusing either way, not a parse.
    private static bool LooksLikeASettingsExport(string raw)
        => raw.StartsWith(Export.SettingsExportFormat.Magic, StringComparison.Ordinal);

    private const string ExportInPlaceOfSettingsDiagnostic =
        "this looks like an exported EmberTern settings file (" + Export.SettingsExportFormat.FileExtension
        + "), not settings.dat. An export is a separate, passphrase-encrypted format — import it from Settings "
        + "instead of copying it over settings.dat.";

    /// <summary>
    /// Persists the whole aggregate — or refuses, leaving the file untouched and the reason in
    /// <see cref="LastSaveDiagnostic"/>.
    /// <para><b>Refusal is the feature.</b> Every section facade reaches this through
    /// <c>Load() ?? new ApplicationSettings()</c>, so if the file on disk cannot be read, the settings being
    /// saved are DEFAULTS standing in for data still sitting in that file. Writing them would destroy
    /// connection profiles, passwords, saved queries, workspace and watches — and the writes that trigger it
    /// are ones the user never thinks of as writes (a grid column resized, a procedure run, the app closed).
    /// Silence is deliberate here: this is not the layer that talks to people, and refusing quietly loses
    /// nothing, whereas writing quietly loses everything. The App tells the user, using
    /// <see cref="LoadWithStatus"/>.</para>
    /// <para>The escape hatch, for a genuinely damaged file, is <see cref="SaveOverUnreadableFile"/> — an
    /// explicit decision that preserves the old bytes first.</para>
    /// </summary>
    public void Save(ApplicationSettings settings)
    {
        LastSaveDiagnostic = null;

        // Never clobber a settings.dat this build could not interpret — whether because a NEWER build wrote it
        // (downgrade protection) or because it could not be decrypted or parsed (audit A-03).
        if (ExistingFileBlocksSave(out var diagnostic))
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

    /// <summary>
    /// Whether <see cref="Save"/> would write, asked <b>before</b> anything is prepared for it.
    /// <para>⭐ Added for the settings IMPORT (etap 5b), and the reason is ordering rather than convenience. An
    /// import copies the current <c>settings.dat</c> aside before it merges; if the save were then refused, that
    /// copy would be a file created for an operation that never happened — and the only ways out of that are a
    /// delete branch on a rule #11 surface or leaving unexplained clutter in the settings folder. Asking first
    /// removes the choice.</para>
    /// <para>⚠ It is the <i>same</i> judgement <see cref="Save"/> makes, from the same file, through the same
    /// private method — not a second opinion that could disagree with it. <see cref="Save"/> still re-checks, so a
    /// file that changes in between is caught there.</para>
    /// </summary>
    /// <returns>True when the file on disk is one this build may replace; otherwise false, with
    /// <paramref name="diagnostic"/> saying why in the words <see cref="LastSaveDiagnostic"/> would use.</returns>
    public bool CanSave(out string diagnostic) => !ExistingFileBlocksSave(out diagnostic);

    /// <summary>
    /// Copies the current <c>settings.dat</c> aside as <c>settings.dat.pre-import-&lt;stamp&gt;</c>, returning the
    /// path, or null when there was no file to copy.
    ///
    /// <para>⚠ <b>A COPY, not a move, and the difference is load-bearing.</b> The naming and the "never delete"
    /// principle come from <see cref="SaveOverUnreadableFile"/>, but its <i>operation</i> does not: that method
    /// renames the old file aside and writes a fresh one over empty ground, whereas an import <b>merges</b> — the
    /// file it is preserving is also the file it is about to read the current values out of. Moving it away would
    /// take the merge base with it, and every unselected section would come back as a default.</para>
    ///
    /// <para>Timestamped for the same reason the unreadable copy is: a second import must not overwrite the first
    /// rescue copy.</para>
    /// </summary>
    public string? CopyAsideForImport()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var preservedAt = _filePath + ".pre-import-" + stamp;
        File.Copy(_filePath, preservedAt, overwrite: true);
        return preservedAt;
    }

    /// <summary>
    /// Writes fresh settings over a file this build cannot interpret, <b>after preserving the old bytes</b>
    /// beside it. The deliberate escape hatch from <see cref="Save"/>'s refusal — and the only way past it.
    /// <para>Nothing calls this automatically, and nothing should: the whole point of the refusal is that a
    /// human decides. It exists so the refusal is a stop rather than a dead end. The old file is renamed, never
    /// deleted, because "cannot read it" is not "it is worthless" — an undecryptable settings.dat is usually
    /// perfectly good data belonging to another Windows account.</para>
    /// </summary>
    /// <returns>The path the previous file was preserved at, or null when there was no file to preserve.</returns>
    public string? SaveOverUnreadableFile(ApplicationSettings settings)
    {
        LastSaveDiagnostic = null;
        string? preservedAt = null;

        if (File.Exists(_filePath))
        {
            // Timestamped, so a second attempt cannot overwrite the first rescue copy.
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            preservedAt = _filePath + ".unreadable-" + stamp;
            File.Move(_filePath, preservedAt, overwrite: true);
        }

        settings.SchemaVersion = CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var payload = _protector.Protect(json);
        var container = SettingsFileContainer.Wrap(
            SettingsFileContainer.CurrentContainerVersion, _protector.Scheme, payload);
        AtomicWrite(_filePath, container);

        return preservedAt;
    }

    // True whenever the file already on disk holds something this build did not fully understand, in which case
    // the settings about to be written are defaults standing in for data we cannot see.
    //
    // ⚠ This method used to be ExistingFileIsFromFuture, and answered only the DOWNGRADE half: a newer
    // container layout, an unknown encryption scheme, or a newer data SchemaVersion. Corrupt and undecryptable
    // files were explicitly allowed through, with the reasoning "never strand the user forever on a genuinely
    // broken file". That reasoning had the trade-off backwards — being stranded is recoverable and visible,
    // whereas the overwrite it permitted was silent and final (audit A-03). SaveOverUnreadableFile is the
    // answer to being stranded; permitting the overwrite never was.
    private bool ExistingFileBlocksSave(out string diagnostic)
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
        catch (IOException ex)
        {
            // We cannot even read it, so we certainly cannot judge it safe to destroy.
            diagnostic = $"Refusing to overwrite settings.dat: it could not be read ({ex.Message}).";
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            diagnostic = $"Refusing to overwrite settings.dat: it could not be read ({ex.Message}).";
            return true;
        }

        // An empty file holds no user data — replacing it destroys nothing.
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        // A settings export sitting in settings.dat's place: refuse, and say which file it actually is. This is
        // the file most worth not destroying — it is the user's portable copy of everything.
        if (LooksLikeASettingsExport(raw))
        {
            diagnostic = "Refusing to overwrite settings.dat: " + ExportInPlaceOfSettingsDiagnostic;
            return true;
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
            if (existing is null)
            {
                diagnostic = "Refusing to overwrite settings.dat: it decrypted but contained no settings.";
                return true;
            }
            if (existing.SchemaVersion > CurrentSchemaVersion)
            {
                diagnostic = $"Refusing to overwrite settings.dat: schema version {existing.SchemaVersion} " +
                             $"is newer than supported {CurrentSchemaVersion}.";
                return true;
            }
        }
        catch (JsonException ex)
        {
            diagnostic = $"Refusing to overwrite settings.dat: it could not be parsed ({ex.Message}).";
            return true;
        }
        catch (Exception ex)
        {
            // THE audit A-03 case. Overwhelmingly a DPAPI mismatch — the file is intact and belongs to another
            // Windows account or machine, so it is exactly the file most worth not destroying.
            diagnostic = $"Refusing to overwrite settings.dat: it could not be decrypted ({ex.Message}). " +
                         "The existing file may be valid for a different Windows account or machine.";
            return true;
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
    //
    // ⭐ internal static (was private), so the SETTINGS IMPORT can call THIS ladder rather than
    // growing one of its own — the export payload is shaped as an ApplicationSettings precisely so
    // that it can. A second migration path would defeat the point of keeping the export's format
    // version separate from this schema version: a future Migrate_2_3 must apply to an imported
    // file for free, and it does. Static because it never used instance state; nothing else about
    // it changed.
    //
    // ⚠ The importer keeps its own "newer than we support" check with its own wording (it must say
    // "this settings export", not "settings.dat"), matching the existing pair of such checks in
    // LoadWithStatus and ExistingFileBlocksSave. Only the LADDER is shared, and only the ladder
    // needs to be.
    internal static void MigrateToCurrentVersion(ApplicationSettings settings)
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
    //
    // The swap now keeps the PREVIOUS file as settings.dat.bak — File.Replace does this in the same atomic
    // operation, so it costs one filename and no extra I/O. It is a secondary net, not the A-03 fix: it holds
    // one generation only, and Save is frequent enough that a second write would roll a bad value through it.
    // ExistingFileBlocksSave is what actually prevents the bad write; this is what makes an ordinary
    // "I saved something I didn't mean to" recoverable by hand.
    private static void AtomicWrite(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        var tempPath = Path.Combine(directory, Path.GetFileName(path) + ".tmp");

        File.WriteAllText(tempPath, content);

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: path + ".bak");
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
