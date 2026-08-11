using EmberTern.Core.Localization;

namespace EmberTern.Core.Settings;

/// <summary>
/// What <see cref="ApplicationSettingsStore"/> says when it will not read, or will not overwrite,
/// <c>settings.dat</c> — decision <b>D‑3</b>'s producer for the settings store.
///
/// <para>⭐ <b>Two families, deliberately not merged.</b> The LOAD family explains why the file could not be
/// read; the REFUSE family explains why it will not be replaced. Several pairs describe the same underlying
/// cause and still read differently in English ("settings.dat could not be read: …" vs "Refusing to overwrite
/// settings.dat: it could not be read (…)"), because they answer different questions at different moments.
/// ⛔ Do not fold them into one key with a shared prefix: the existing English wordings would have to change,
/// and a refusal is not a read failure — the second one is about a write that is being prevented.</para>
///
/// <para>⚠ <b>Every <c>{n}</c> is DATA</b> — a version number, or an exception's own message. The exception
/// text is the platform speaking (a file lock, a DPAPI failure) and travels verbatim, exactly as Firebird's
/// does in <c>FirebirdConnectionMessages</c>: our sentence is the key, theirs is an argument.</para>
/// </summary>
public static class SettingsStoreMessages
{
    // ── Load: why the file could not be read ─────────────────────────────────────────────────────────

    /// <summary>An <c>.etsettings</c> export copied over <c>settings.dat</c>.</summary>
    public static readonly MessageKey ExportInPlaceOfSettings =
        new("Settings.Load.ExportInPlaceOfSettings");

    /// <summary>Found container version <c>{0}</c>, supported <c>{1}</c>.</summary>
    public static readonly MessageKey ContainerVersionNewer = new("Settings.Load.ContainerVersionNewer");

    /// <summary>Scheme name as <c>{0}</c>.</summary>
    public static readonly MessageKey UnknownEncryptionScheme = new("Settings.Load.UnknownEncryptionScheme");

    public static readonly MessageKey DecryptedButEmpty = new("Settings.Load.DecryptedButEmpty");

    /// <summary>Found schema version <c>{0}</c>, supported <c>{1}</c>.</summary>
    public static readonly MessageKey SchemaVersionNewer = new("Settings.Load.SchemaVersionNewer");

    /// <summary>The parser's own message as <c>{0}</c>.</summary>
    public static readonly MessageKey CouldNotBeParsed = new("Settings.Load.CouldNotBeParsed");

    /// <summary>The I/O error's own message as <c>{0}</c>.</summary>
    public static readonly MessageKey CouldNotBeRead = new("Settings.Load.CouldNotBeRead");

    /// <summary>The crypto error's own message as <c>{0}</c>.</summary>
    public static readonly MessageKey CouldNotBeDecrypted = new("Settings.Load.CouldNotBeDecrypted");

    // ── Refuse: why the file will not be overwritten ─────────────────────────────────────────────────

    /// <summary>The I/O error's own message as <c>{0}</c>.</summary>
    public static readonly MessageKey RefuseCouldNotBeRead = new("Settings.Refuse.CouldNotBeRead");

    public static readonly MessageKey RefuseExportInPlace = new("Settings.Refuse.ExportInPlace");

    /// <summary>Found container version <c>{0}</c>, supported <c>{1}</c>.</summary>
    public static readonly MessageKey RefuseContainerVersion = new("Settings.Refuse.ContainerVersion");

    /// <summary>Scheme name as <c>{0}</c>.</summary>
    public static readonly MessageKey RefuseUnknownScheme = new("Settings.Refuse.UnknownScheme");

    public static readonly MessageKey RefuseDecryptedButEmpty = new("Settings.Refuse.DecryptedButEmpty");

    /// <summary>Found schema version <c>{0}</c>, supported <c>{1}</c>.</summary>
    public static readonly MessageKey RefuseSchemaVersion = new("Settings.Refuse.SchemaVersion");

    /// <summary>The parser's own message as <c>{0}</c>.</summary>
    public static readonly MessageKey RefuseCouldNotBeParsed = new("Settings.Refuse.CouldNotBeParsed");

    /// <summary>The crypto error's own message as <c>{0}</c>.</summary>
    public static readonly MessageKey RefuseCouldNotBeDecrypted = new("Settings.Refuse.CouldNotBeDecrypted");

    // ── Write ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The I/O error's own message as <c>{0}</c>.</summary>
    public static readonly MessageKey CouldNotBeWritten = new("Settings.Write.CouldNotBeWritten");

    public static readonly MessageKey LockedByAnotherInstance = new("Settings.Write.LockedByAnotherInstance");
}
