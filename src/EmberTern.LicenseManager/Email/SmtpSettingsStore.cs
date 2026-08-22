using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.ViewModels;

namespace EmberTern.LicenseManager.Email;

/// <summary>What a read of the settings file found. ⭐ Four ANSWERS, not a bool and a shrug.</summary>
public enum SmtpSettingsState
{
    /// <summary>⭐ No file yet — e-mail has never been configured. A first run, not a failure.</summary>
    NotConfigured = 0,

    /// <summary>Read in full, password included.</summary>
    Loaded = 1,

    /// <summary>
    /// ⭐ The settings are readable but the password is not — the file was written by another Windows
    /// account or on another machine. Everything except the password is usable and the operator only has
    /// to retype one field.
    /// </summary>
    PasswordUnavailable = 2,

    /// <summary>⛔ The file exists and could not be understood at all.</summary>
    Unreadable = 3,
}

/// <summary>The outcome of a read: what state, what settings, and — when something is wrong — why.</summary>
/// <param name="State">Which of the four answers.</param>
/// <param name="Settings">Whatever could be recovered. Never <see langword="null"/>.</param>
/// <param name="Problem">A sentence for the operator, or <see langword="null"/> when nothing is wrong.</param>
public sealed record SmtpSettingsLoad(
    SmtpSettingsState State, SmtpSettings Settings, LocalizedText? Problem);

/// <summary>
/// Reads and writes <c>smtp.dat</c>.
///
/// <para>⭐⭐ <b>The four states above are the point of this class, and they exist because of a defect this
/// project already carries elsewhere.</b> <c>PreferencesService</c> turns a failed read into validated
/// DEFAULTS, so a transient failure serves defaults for the session and the next save persists them as if
/// the user had chosen them (recorded in <c>docs/current-state.md</c> as deferred debt). ⛔ A store that
/// answers "here are your settings" to both <i>"there are none yet"</i> and <i>"I could not read them"</i>
/// cannot avoid that class of bug, however careful its callers are. So this one distinguishes them, and
/// the window shows a different thing for each.</para>
///
/// <para>⭐ <b>Plaintext in memory, ciphertext at rest.</b> The password travels on
/// <see cref="SmtpSettings"/> and is protected HERE, at the I/O boundary — the same arrangement
/// EmberTern's <c>SecretProtector</c> established, and the reason no caller can forget to protect it:
/// this is the only code that writes the record to disk.</para>
///
/// <para>⚠ <b>Deliberately different from <c>settings.dat</c>'s rule that a save REFUSES over a file it
/// cannot read.</b> That rule protects connection profiles, passwords and workspace — data the user
/// cannot retype. This file holds six fields the operator is looking at while they save, so refusing
/// would trap them: a corrupted <c>smtp.dat</c> could never be repaired from the window that exists to
/// edit it. ⛔ The difference is the blast radius, not a relaxation of the principle.</para>
///
/// <para>⛔ <b>This file is NOT part of a register backup.</b> §12.3 keeps "back up the register" and
/// "back up the key" as separate operations with separate risk; this is a third such file, and a DPAPI
/// CurrentUser blob would not survive the move to another machine anyway.</para>
/// </summary>
public sealed class SmtpSettingsStore
{
    /// <summary>
    /// The container version this build writes. A newer one is refused rather than guessed at.
    ///
    /// <para>⭐ <b>v2 (L6.1a) added <c>messageLanguage</c>; v3 (L10.1) added <c>bulkDelaySeconds</c>
    /// and <c>bulkMaxPerRun</c> — and v1 and v2 both still read correctly.</b> Every stored field is
    /// nullable by design (§13.4's forward-compatibility rule applied to this file), so an older file
    /// simply has no value for the newer keys and takes their defaults — there is no migration step, no
    /// rewrite on read, and nothing an operator has to do. ⚠ This is the SETTINGS file's version;
    /// ⛔ the register schema is untouched and stays at 2.</para>
    /// </summary>
    public const int CurrentVersion = 3;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    /// <summary>Creates a store over an explicit file path.</summary>
    /// <remarks>
    /// ⭐ A path rather than a <see cref="ManagerPaths"/>, so every state here is reachable in a test
    /// without touching the operator's real <c>%APPDATA%</c> — the same posture
    /// <see cref="Data.RegisterBackup"/> takes by working on bytes.
    /// </remarks>
    public SmtpSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>Creates a store at the standard location.</summary>
    public static SmtpSettingsStore At(ManagerPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new SmtpSettingsStore(paths.SmtpSettings);
    }

    /// <summary>The file this store reads and writes.</summary>
    public string FilePath => _path;

    /// <summary>Reads the settings, reporting which of the four states was found.</summary>
    public SmtpSettingsLoad Load()
    {
        if (!File.Exists(_path))
        {
            return new SmtpSettingsLoad(SmtpSettingsState.NotConfigured, SmtpSettings.Empty, null);
        }

        Stored? stored;
        try
        {
            stored = JsonSerializer.Deserialize<Stored>(File.ReadAllBytes(_path), Json);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new SmtpSettingsLoad(
                SmtpSettingsState.Unreadable,
                SmtpSettings.Empty,
                new LocalizedText(StatusCatalog.SmtpFileNotRead, e.Message));
        }

        if (stored is null)
        {
            return new SmtpSettingsLoad(
                SmtpSettingsState.Unreadable,
                SmtpSettings.Empty,
                new LocalizedText(StatusCatalog.SmtpFileEmpty));
        }

        if (stored.Version > CurrentVersion)
        {
            // ⛔ Forward-compatibility: refuse rather than read a subset and write it back, which would
            //    silently DELETE whatever a newer build had stored.
            return new SmtpSettingsLoad(
                SmtpSettingsState.Unreadable,
                SmtpSettings.Empty,
                new LocalizedText(StatusCatalog.SmtpFileFromNewerBuild, stored.Version));
        }

        var settings = new SmtpSettings
        {
            Host = stored.Host ?? string.Empty,
            Port = stored.Port ?? SmtpSettings.DefaultPort,
            Security = stored.Security ?? SmtpSecurity.StartTls,
            FromAddress = stored.FromAddress ?? string.Empty,
            FromName = stored.FromName ?? string.Empty,
            Username = stored.Username ?? string.Empty,

            // ⭐ A v1 file has no language at all, and that is not a defect — it takes the default. ⛔ Not
            //   `Resolve` here: an unrecognised code is kept as WRITTEN so the settings window can say
            //   what it found, and is resolved only at the moment a message is composed.
            MessageLanguage = stored.MessageLanguage ?? MessageLanguages.Default,

            // ⭐⭐ v2 → v3: absent in every file written before L10.1, so a v2 file takes the
            //    DEFAULTS and there is no migration step, no rewrite on read and nothing an operator
            //    has to do. ⚠ The value is taken as WRITTEN and is not repaired here — the same
            //    posture as `messageLanguage`: `Validate` is what reports an out-of-range number, so the
            //    settings window can SAY what it found instead of silently showing something else.
            BulkDelaySeconds = stored.BulkDelaySeconds ?? SmtpSettings.DefaultBulkDelaySeconds,
            BulkMaxPerRun = stored.BulkMaxPerRun ?? SmtpSettings.DefaultBulkMaxPerRun,
        };

        if (string.IsNullOrEmpty(stored.Password))
        {
            return new SmtpSettingsLoad(SmtpSettingsState.Loaded, settings, null);
        }

        if (!LocalDpapiProtector.TryUnprotect(stored.Password, out var password))
        {
            // ⭐ Everything else is still good — only the one field the operator can retype is missing.
            return new SmtpSettingsLoad(
                SmtpSettingsState.PasswordUnavailable,
                settings,
                new LocalizedText(StatusCatalog.SmtpPasswordNotDecrypted));
        }

        return new SmtpSettingsLoad(SmtpSettingsState.Loaded, settings with { Password = password }, null);
    }

    /// <summary>
    /// Writes the settings, protecting the password on the way out.
    ///
    /// <para>⭐ Atomic, through <see cref="SigningSession.WriteAtomic"/> — the same writer the keystore
    /// uses. ⛔ A third copy of "temp file then replace" was deliberately not made.</para>
    /// </summary>
    public void Save(SmtpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stored = new Stored
        {
            Version = CurrentVersion,
            Host = settings.Host,
            Port = settings.Port,
            Security = settings.Security,
            FromAddress = settings.FromAddress,
            FromName = settings.FromName,
            Username = settings.Username,
            MessageLanguage = settings.MessageLanguage,
            BulkDelaySeconds = settings.BulkDelaySeconds,
            BulkMaxPerRun = settings.BulkMaxPerRun,
            Password = LocalDpapiProtector.Protect(settings.Password),
            PasswordProtection = ProtectionLabel,
        };

        SigningSession.WriteAtomic(
            _path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(stored, Json)));
    }

    /// <summary>Forgets the stored settings entirely. ⭐ The operator's way to undo a configuration.</summary>
    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    /// <summary>
    /// ⭐ Recorded in the file so a later build can tell HOW the password was protected before trying to
    /// read it — the same reason EmberTern's settings container carries its scheme in a cleartext header.
    /// </summary>
    private const string ProtectionLabel = "dpapi-currentuser";

    // ⚠ The on-disk shape. Everything nullable so that a field added later reads as absent rather than
    //   as a parse failure — §13.4's forward-compatibility rule, applied to this file.
    private sealed record Stored
    {
        [JsonPropertyName("version")] public int Version { get; init; }

        [JsonPropertyName("host")] public string? Host { get; init; }

        [JsonPropertyName("port")] public int? Port { get; init; }

        [JsonPropertyName("security")] public SmtpSecurity? Security { get; init; }

        [JsonPropertyName("fromAddress")] public string? FromAddress { get; init; }

        [JsonPropertyName("fromName")] public string? FromName { get; init; }

        [JsonPropertyName("username")] public string? Username { get; init; }

        /// <summary>⭐ Added in v2. Absent in a v1 file, which then takes the default.</summary>
        [JsonPropertyName("messageLanguage")] public string? MessageLanguage { get; init; }

        /// <summary>⭐ Added in v3 (L10.1). Absent in a v1 or v2 file, which then takes the default.</summary>
        [JsonPropertyName("bulkDelaySeconds")] public int? BulkDelaySeconds { get; init; }

        /// <summary>⭐ Added in v3 (L10.1). Absent in a v1 or v2 file, which then takes the default.</summary>
        [JsonPropertyName("bulkMaxPerRun")] public int? BulkMaxPerRun { get; init; }

        /// <summary>⛔ Ciphertext, always. Never a readable password.</summary>
        [JsonPropertyName("password")] public string? Password { get; init; }

        [JsonPropertyName("passwordProtection")] public string? PasswordProtection { get; init; }
    }
}
