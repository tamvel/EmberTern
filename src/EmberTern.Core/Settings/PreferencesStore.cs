using EmberTern.Core.Security;

namespace EmberTern.Core.Settings;

/// <summary>
/// The 8th section facade over the unified <c>ApplicationSettingsStore</c> (settings.dat), mirroring
/// <c>WorkspaceStore</c> / <c>GridProfileStore</c> / <c>WatchStore</c> / <c>ImportProfileStore</c>. Owns
/// <c>UserSettings.Preferences</c>. Every write is read-modify-write on that one section, so it can never
/// clobber Connections / Folders / Workspace / GridProfiles / ParameterHistory / DebugWatches /
/// ImportProfiles in the shared file.
///
/// <para><b>⭐ This facade has exactly one job beyond persistence: it normalizes at the file boundary
/// (ratified — design §5.2.1).</b> <see cref="Preferences"/> owns the defaults; this class owns bringing
/// whatever was actually on disk into a usable shape, and supplies no defaults of its own — every fallback
/// it applies is the model's own, reached through <see cref="PreferenceOptions"/>.</para>
///
/// <para>Three properties of that job are load-bearing:</para>
/// <list type="number">
///   <item><description>
///     <b>Normalization is silent and TOTAL, never rejection.</b> An unrecognised value is corrected and the
///     load continues; every field is valid when <see cref="Load"/> returns, always. This store never
///     refuses to read because a value was bad.
///   </description></item>
///   <item><description>
///     ⚠ <b><see cref="Load"/> never writes.</b> The correction lives in memory and reaches disk only if
///     something later saves for its own reasons. A "repair the file on load" write is precisely the shape
///     audit A-03 was about, and <c>ApplicationSettingsStore.Save</c>'s refusal exists to stop it.
///   </description></item>
///   <item><description>
///     <b>Normalization runs in both directions.</b> Writing is also a boundary crossing, and a value we
///     would only have to correct on the next read has no business reaching the file. <see cref="Validate"/>
///     is idempotent, so the two directions cannot fight.
///   </description></item>
/// </list>
///
/// <para><b>⚠ There are deliberately NO per-property setters</b> — no <c>SetTheme(string)</c>, no
/// <c>Save(string key, string value)</c>. <see cref="Save"/> takes a whole <see cref="Preferences"/>, which
/// is to say a <i>settled</i> value, and that is an architectural decision rather than a stylistic one
/// (design §5.5.1):</para>
/// <list type="bullet">
///   <item><description>
///     A save is far more expensive than it looks. <c>Save</c> calls <c>ExistingFileBlocksSave</c> first,
///     which does a <b>full read + decrypt + deserialize of the entire settings.dat</b>, before the
///     serialize + encrypt + temp write + <c>File.Replace</c> + <c>.bak</c> roll — roughly seven file
///     operations and two DPAPI round-trips per call. Avalonia's <c>TextBox</c> updates its binding on every
///     keystroke by default, so a streaming API would make typing <c>5000</c> into a numeric field four
///     complete encrypted rewrites of every setting the user owns.
///   </description></item>
///   <item><description>
///     ⭐ And the consequence that is not performance: <c>TryAtomicWrite</c> keeps exactly <b>one</b> generation
///     of <c>settings.dat.bak</c>. Four keystrokes roll it through four generations, destroying the pre-edit
///     state the hardening sprint added it to preserve — the one hand-recovery net, gone at precisely the
///     moment someone is editing settings. That backup's value depends on saves being <i>deliberate</i>.
///   </description></item>
/// </list>
/// <para>So a settings page commits discrete controls (radio, checkbox, ComboBox) immediately on selection,
/// and free-text or numeric fields on blur or Enter — never per keystroke. This API is shaped so the cheap
/// mistake is not available.</para>
/// </summary>
public sealed class PreferencesStore
{
    private readonly ApplicationSettingsStore _settings;

    public PreferencesStore()
        : this(new ApplicationSettingsStore())
    {
    }

    public PreferencesStore(SecretProtector protector)
        : this(new ApplicationSettingsStore(protector))
    {
    }

    public PreferencesStore(string directory)
        : this(new ApplicationSettingsStore(directory))
    {
    }

    public PreferencesStore(string directory, SecretProtector? protector)
        : this(new ApplicationSettingsStore(directory, protector))
    {
    }

    private PreferencesStore(ApplicationSettingsStore settings)
    {
        _settings = settings;
    }

    public string FilePath => _settings.FilePath;

    /// <summary>
    /// Why the last <see cref="Save"/> did not write, or null when it did.
    /// <para>Forwarded from the underlying store rather than re-derived, so there is no second health
    /// mechanism to keep in step with the first.</para>
    /// </summary>
    public string? LastSaveDiagnostic => _settings.LastSaveDiagnostic;

    /// <summary>The same refusal as a <see cref="Localization.LocalizableMessage"/> (D‑3). Forwarded, never
    /// re-derived.</summary>
    public Localization.LocalizableMessage? LastSaveMessage => _settings.LastSaveMessage;

    /// <summary>
    /// The stored preferences, normalized — <b>never null, and every field valid</b>, whatever was on disk.
    /// <para>
    /// A missing file, an empty one, a corrupt one and an undecryptable one all yield defaults here, because
    /// none of them is a preference this build can honour. That deliberately does <b>not</b> make it safe to
    /// write: <c>ApplicationSettingsStore.Save</c> tells the two apart from the same file and refuses over
    /// the ones that hold data it could not read (audit A-03).
    /// </para>
    /// <para>Never writes — see the class remarks.</para>
    /// </summary>
    public Preferences Load() => Validate(_settings.Load()?.UserSettings.Preferences);

    /// <summary>
    /// Persists <paramref name="preferences"/>, normalized, leaving every other section untouched.
    /// </summary>
    /// <returns>
    /// <c>true</c> when it wrote; <c>false</c> when the underlying store refused, with the reason in
    /// <see cref="LastSaveDiagnostic"/>.
    /// <para>
    /// ⚠ <b>The caller must act on this.</b> <c>Save</c> refuses silently over a settings.dat this build
    /// could not read — correct behaviour, because the values being written would be defaults standing in
    /// for data still sitting in that file. Silence is right for every other writer in the app, which are all
    /// incidental (a grid column resized, a procedure run). A surface whose explicit purpose is <i>"change
    /// this setting"</i> is the one place it is wrong: a dialog that appears to accept a change and persists
    /// nothing is the worst possible place for that silence.
    /// </para>
    /// </returns>
    public bool Save(Preferences preferences)
    {
        var settings = _settings.Load() ?? new ApplicationSettings();
        settings.UserSettings.Preferences = Validate(preferences);
        _settings.Save(settings);
        return _settings.LastSaveDiagnostic is null;
    }

    /// <summary>
    /// Normalizes a <see cref="Preferences"/> read from (or bound for) the file: each enumerated property is
    /// brought to a legal value through its own <see cref="PreferenceOptions"/> set, and everything else is
    /// carried over untouched.
    /// <para>
    /// ⭐ <b>The contract this must satisfy, and it is pinned by a test:</b>
    /// <c>Validate(new Preferences())</c> equals <c>new Preferences()</c>. That single assertion holds the
    /// model and this method against each other and fails the build the day a property's initializer is
    /// something the validator would reject — for instance a <c>Language = "pl"</c> default while the catalog
    /// still has one row. Neither half looks wrong on its own, which is exactly why it is worth pinning.
    /// </para>
    /// <para>
    /// ⚠ <b>It returns <c>source with { … }</c>, not a fresh instance, and that is deliberate.</b> A fresh
    /// instance would silently reset any property somebody forgets to list here — turning "I added a
    /// preference" into "that preference never persists", which is a data-loss shape rather than a cosmetic
    /// one. With <c>with</c>, an unlisted property passes through, which is also the right answer for a
    /// future free-text preference that has nothing to normalize against.
    /// </para>
    /// <para>Idempotent by construction, so applying it on read and on write cannot fight.</para>
    /// </summary>
    public static Preferences Validate(Preferences? source)
    {
        // Null is reachable from a real file: `"Preferences": null` deserializes to null even though the
        // property is non-nullable. Total normalization means answering that too, not throwing on it.
        if (source is null)
        {
            return new Preferences();
        }

        return source with
        {
            Theme = PreferenceOptions.Theme.Normalize(source.Theme),
            Language = PreferenceOptions.Language.Normalize(source.Language),
            FormatterKeywordCase = PreferenceOptions.Casing.Normalize(source.FormatterKeywordCase),
            FormatterIdentifierCase = PreferenceOptions.Casing.Normalize(source.FormatterIdentifierCase),
            DebuggerIsolation = PreferenceOptions.DebuggerIsolation.Normalize(source.DebuggerIsolation),
            TabStripMode = PreferenceOptions.TabStripMode.Normalize(source.TabStripMode),

            // Numeric preferences are clamped, not reset (see PreferenceRange): a stored 50 000 000 means "as
            // many as possible", and answering it with the shipped 5 000 would be data loss with extra steps.
            PreviewRowLimit = PreferenceOptions.PreviewRowLimit.Normalize(source.PreviewRowLimit),
            FullLoadPromptThreshold =
                PreferenceOptions.FullLoadPromptThreshold.Normalize(source.FullLoadPromptThreshold),
            DataPageSize = PreferenceOptions.DataPageSize.Normalize(source.DataPageSize),
            TabStripMaxRows = PreferenceOptions.TabStripMaxRows.Normalize(source.TabStripMaxRows),

            // ⚠ The booleans are absent on purpose, and it is a decision rather than an omission: a bool has no
            // illegal value to correct, so listing it here could only be a no-op — and `source with { … }` is
            // what carries an unlisted property through untouched (§12.1d). PreferencesTests' declared table
            // records that decision per property and fails the build when a new one has no entry.
        };
    }
}
