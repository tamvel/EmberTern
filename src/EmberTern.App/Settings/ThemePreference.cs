using Avalonia;
using Avalonia.Styling;
using EmberTern.Core.Settings;

namespace EmberTern.App.Settings;

/// <summary>
/// The ONE place a stored theme key (<c>"Dark"</c> / <c>"Light"</c>) becomes an Avalonia
/// <see cref="ThemeVariant"/>, and the one place the variant is assigned.
///
/// <para>Three callers need this — the startup read, the titlebar toggle and the Settings Center radio — and
/// before this existed the assignment was written in one of them by hand. Two mappings would be two answers
/// to "what does Light mean", and the failure would be silent: a theme that applies from one surface and not
/// the other.</para>
///
/// <para>⚠ <b>Nothing here reads or writes the store.</b> The preference travels as a string
/// (<see cref="PreferencesService"/>); this class only translates and paints. That split is what lets a view
/// model own the value without ever naming an Avalonia type (architecture rule #1, decision Q5).</para>
///
/// <para>⚠ <b>The startup order in <c>App</c> is load-bearing (design §2.1).</b> <c>App.axaml</c> hard-codes
/// <c>RequestedThemeVariant="Dark"</c>, and that stays: it is the value the framework has before any store is
/// read. Removing it and relying on this class alone would leave <c>ThemeVariant.Default</c> in the window
/// between XAML load and the first <see cref="Apply"/> — and <c>Default</c> follows the <i>OS</i> theme, which
/// reads exactly like a regression to every existing user.</para>
/// </summary>
public static class ThemePreference
{
    /// <summary>Stored key → variant. Anything unrecognised is Dark, matching
    /// <c>PreferenceOptions.Theme.Default</c> — though the store normalizes first, so this is a second net
    /// rather than the primary one.</summary>
    public static ThemeVariant VariantFor(string? themeKey)
        => string.Equals(themeKey, PreferenceOptions.ThemeLight, System.StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

    /// <summary>Variant → stored key. Used by the titlebar toggle, which flips what is on screen and then has
    /// to say which preference that is.</summary>
    public static string KeyFor(ThemeVariant variant)
        => variant == ThemeVariant.Light ? PreferenceOptions.ThemeLight : PreferenceOptions.ThemeDark;

    /// <summary>The opposite key — the toggle's whole decision, expressed once.</summary>
    public static string Toggle(string? themeKey)
        => VariantFor(themeKey) == ThemeVariant.Light
            ? PreferenceOptions.ThemeDark
            : PreferenceOptions.ThemeLight;

    /// <summary>Paints the application in the stored theme. A no-op when there is no application (design-time,
    /// some headless paths) rather than a throw — this is presentation, and failing to paint must never be the
    /// thing that stops a session.</summary>
    public static void Apply(string? themeKey)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = VariantFor(themeKey);
        }
    }
}
