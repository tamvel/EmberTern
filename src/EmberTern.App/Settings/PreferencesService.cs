using System;
using EmberTern.Core.Settings;

namespace EmberTern.App.Settings;

/// <summary>
/// ⭐ The app's ONE in-memory owner of the current <see cref="Preferences"/>, sitting over the Core
/// <see cref="PreferencesStore"/>.
///
/// <para><b>Why this exists at all — it is a direct consequence of etap 2's ratified API shape, not an extra
/// layer for its own sake.</b> <see cref="PreferencesStore"/> deliberately has no per-property setters:
/// <c>Save</c> takes a whole <see cref="Preferences"/>, so that a settings page commits a <i>settled</i> value
/// rather than streaming keystrokes into a file whose every write costs ~7 file operations, 2 DPAPI round
/// trips and one generation of <c>settings.dat.bak</c> (design §5.5.1 / §12.3). The consequence in the App
/// layer is that two holders of a <see cref="Preferences"/> snapshot <b>clobber each other</b>: the titlebar
/// theme toggle would write <c>Theme</c>, and a Settings Center that had loaded earlier would write its own
/// stale copy back over it the next time any other row changed. One owner removes that by construction.</para>
///
/// <para>Three consumers share this one instance: the startup theme read (<c>App</c>), the titlebar toggle
/// (<c>MainWindow</c> code-behind, per architecture rule #1 / decision Q5), and Settings Center. None of them
/// keeps its own snapshot.</para>
///
/// <para>⚠ <b>Zero Avalonia here on purpose.</b> This class moves <i>strings</i>; turning a theme key into a
/// <c>ThemeVariant</c> is <see cref="ThemePreference"/>'s job, which is what keeps a preference a preference
/// and not a UI type in disguise.</para>
/// </summary>
public sealed class PreferencesService
{
    private readonly PreferencesStore _store;
    private Preferences _current;

    public PreferencesService(PreferencesStore store)
    {
        _store = store;
        // Never null and every field valid, whatever was on disk — the etap-2 contract, which is why there is
        // no null check and no bootstrap call anywhere downstream.
        _current = store.Load();
    }

    /// <summary>The live preferences. Always valid; never null.</summary>
    public Preferences Current => _current;

    /// <summary>
    /// Why the last <see cref="Apply"/> did not reach the file, or null when it did. Forwarded from the store,
    /// so there is no second health mechanism beside <c>ApplicationSettingsStore</c>'s.
    /// </summary>
    public string? LastSaveDiagnostic => _store.LastSaveDiagnostic;

    /// <summary>Raised after <see cref="Current"/> changes — including when the write was refused, because the
    /// session honours the choice either way (see <see cref="Apply"/>).</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Adopts <paramref name="updated"/> as the live preferences and persists it.
    /// </summary>
    /// <returns><c>true</c> when it reached the file; <c>false</c> when the store refused, with the reason in
    /// <see cref="LastSaveDiagnostic"/>.</returns>
    /// <remarks>
    /// ⭐ <b>The in-memory value is adopted even when the save is refused</b>, and that is deliberate. A refusal
    /// means <i>this file cannot be written</i> (audit A-03: it holds data this build could not read), not
    /// <i>this choice is invalid</i> — so refusing to honour it for the session as well would punish the user
    /// twice for a file problem they have already been told about. The surface that asked for the change is
    /// what must say it did not persist (design §5.5); that is what the return value is for.
    /// </remarks>
    public bool Apply(Preferences updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        // Normalize here as well as inside the store, so Current is the same value a reload would produce —
        // otherwise the session could hold "dark" while the file holds "Dark". Validate is idempotent.
        _current = PreferencesStore.Validate(updated);
        var persisted = _store.Save(_current);
        Changed?.Invoke(this, EventArgs.Empty);
        return persisted;
    }
}
