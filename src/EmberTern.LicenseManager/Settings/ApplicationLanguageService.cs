using System;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.Services;

namespace EmberTern.LicenseManager.Settings;

/// <summary>
/// ⭐⭐ The ONE place the interface language is applied — for the startup and for the picker alike.
/// </summary>
/// <remarks>
/// <para><b>It exists because L8.5 gave the operator a way to change the language, and there had to be
/// exactly one way for it to take effect.</b> Until then the composition root read the preference and
/// called <see cref="Loc.Apply"/> itself, which was correct while startup was the only path;
/// <c>TheLanguage_IsAppliedInExactlyOnePlace</c> asserted that. A picker calling <c>Loc.Apply</c> on its
/// own would have made that two paths — and two paths is how a saved preference and a rendered window
/// start disagreeing. ⭐ So the seam moved here and the guard still names ONE file; it simply names a file
/// whose job this is, rather than the composition root.</para>
///
/// <para>⛔ <b>The language still comes from the preference and from nowhere else.</b> Nothing here reads
/// <c>CurrentUICulture</c>, an environment variable or the operating system — <see cref="Restore"/> takes
/// the stored value, <see cref="Choose"/> takes the operator's, and there is no third entry point.</para>
///
/// <para>⚠ <see cref="Choose"/> applies the language <b>even when the store refuses the write</b>, and that
/// order is deliberate: a disk that cannot be written is not a reason to leave the operator looking at a
/// language they just asked to leave. The caller learns the write failed and can say so; the interface
/// still follows the choice for this session.</para>
/// </remarks>
public sealed class ApplicationLanguageService
{
    private readonly ManagerPreferencesStore _store;

    /// <summary>Creates the service over a preferences store.</summary>
    public ApplicationLanguageService(ManagerPreferencesStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>The service over the application's own preferences file.</summary>
    public static ApplicationLanguageService At(ManagerPaths paths) =>
        new(ManagerPreferencesStore.At(paths));

    /// <summary>Applies the stored preference. ⭐ Called by the composition root at startup.</summary>
    /// <returns>The language code that was applied.</returns>
    public string Restore()
    {
        var stored = _store.Load().Language;
        Use(stored);
        return stored;
    }

    /// <summary>Stores the operator's choice and applies it. ⭐ Called by the settings picker.</summary>
    /// <returns><see langword="false"/> when the choice could not be written — see the type's remarks.</returns>
    public bool Choose(string? languageKey)
    {
        var code = ApplicationLanguages.Resolve(languageKey);

        // ⚠⚠ THROUGH Update, never through Save with a fresh object. `Save` persists the WHOLE record, so
        //    `new ManagerPreferences { Language = code }` would silently reset every other preference to
        //    its default — which is exactly what it did for the one day between this file gaining a second
        //    member and this line being corrected. See ManagerPreferencesStore.Update.
        var saved = _store.Update(preferences => preferences with { Language = code });

        Use(code);
        return saved;
    }

    /// <summary>The language the store currently holds, without applying it.</summary>
    public string Stored => _store.Load().Language;

    // ⭐⭐ THE one call. Both entry points funnel through it, which is what keeps
    //    TheLanguage_IsAppliedInExactlyOnePlace a true statement about the application rather than about
    //    the composition root's convenience.
    private static void Use(string code) => Loc.Apply(code);
}
