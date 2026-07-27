namespace EmberTern.Core.Settings;

/// <summary>
/// What happened when <see cref="ApplicationSettingsStore.LoadWithStatus"/> tried to read <c>settings.dat</c>.
/// <para>
/// This distinction exists because it was once absent, and its absence was a data-loss bug (audit A-03).
/// <c>Load()</c> returned <c>null</c> for "fresh install" AND for "this file exists but I cannot read it",
/// and every section facade answers <c>null</c> with <c>?? new ApplicationSettings()</c> followed by a
/// <c>Save</c>. So on a machine where DPAPI could not decrypt the file — a copied profile, a restored user
/// account — the next trivial preference write replaced connection profiles, passwords, saved queries,
/// workspace and watch expressions with defaults. And those writes are frequent and silent: a grid column
/// resize, a procedure run recording its parameters, closing the app.
/// </para>
/// <para><b>The rule these states encode:</b> only <see cref="Missing"/> and <see cref="Loaded"/> permit a
/// write. Anything else means there is a file on disk holding user data that this build cannot interpret, and
/// overwriting data we cannot read is the exact failure Architecture rule #11 forbids.</para>
/// </summary>
public enum SettingsLoadStatus
{
    /// <summary>No settings file (fresh install, or a new machine). Defaults are correct, and saving is safe
    /// because there is nothing to destroy.</summary>
    Missing,

    /// <summary>Read and understood.</summary>
    Loaded,

    /// <summary>The file exists but could not be decrypted — the overwhelmingly likely cause being DPAPI on a
    /// different Windows account or machine. <b>It may well decrypt perfectly on the right machine</b>, which
    /// is precisely why it must not be replaced.</summary>
    Unreadable,

    /// <summary>The file was read and decrypted but is not valid settings — a truncated or damaged payload.
    /// Recoverable only by a decision the user makes, never silently.</summary>
    Corrupt,

    /// <summary>Written by a NEWER EmberTern build (newer container layout, an encryption scheme this build
    /// has no protector for, or a newer data schema version). Downgrade protection: this build cannot
    /// represent the newer data, so writing would silently drop whatever it does not understand.</summary>
    FutureVersion,
}

/// <summary>
/// The outcome of a settings load: the status, the settings when there are any, and a human-readable
/// diagnostic when there are not.
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="Settings">The loaded settings — non-null only for <see cref="SettingsLoadStatus.Loaded"/>.</param>
/// <param name="Diagnostic">
/// Why the load produced nothing, phrased for a person. Null for <see cref="SettingsLoadStatus.Loaded"/> and
/// <see cref="SettingsLoadStatus.Missing"/>, which need no explanation.
/// </param>
public readonly record struct SettingsLoadResult(
    SettingsLoadStatus Status,
    ApplicationSettings? Settings,
    string? Diagnostic)
{
    /// <summary>
    /// Whether saving is safe. True only when there is nothing on disk to lose
    /// (<see cref="SettingsLoadStatus.Missing"/>) or when what is there was fully understood
    /// (<see cref="SettingsLoadStatus.Loaded"/>).
    /// </summary>
    public bool CanSave => Status is SettingsLoadStatus.Missing or SettingsLoadStatus.Loaded;

    /// <summary>
    /// Whether a file exists that holds user data this build cannot interpret — the state the user has to be
    /// TOLD about, because until it is resolved their preferences are not being persisted.
    /// </summary>
    public bool NeedsAttention => Status is SettingsLoadStatus.Unreadable
        or SettingsLoadStatus.Corrupt
        or SettingsLoadStatus.FutureVersion;

    internal static SettingsLoadResult Missing() => new(SettingsLoadStatus.Missing, null, null);
    internal static SettingsLoadResult Loaded(ApplicationSettings settings) => new(SettingsLoadStatus.Loaded, settings, null);
    internal static SettingsLoadResult Unreadable(string diagnostic) => new(SettingsLoadStatus.Unreadable, null, diagnostic);
    internal static SettingsLoadResult Corrupt(string diagnostic) => new(SettingsLoadStatus.Corrupt, null, diagnostic);
    internal static SettingsLoadResult Future(string diagnostic) => new(SettingsLoadStatus.FutureVersion, null, diagnostic);
}
