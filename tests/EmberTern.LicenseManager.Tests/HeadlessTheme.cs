using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// Switching the theme, and reading a brush out of the linked palette, for every headless test class in
/// this assembly.
///
/// <para>⭐ Extracted at L5.1, when a second headless class was about to make a second copy. The two
/// operations are one line each, which is exactly why two copies would have gone unnoticed — and a
/// per-class idea of "how you switch the theme in a test" is how two classes end up testing two
/// different things while appearing to test one.</para>
/// </summary>
internal static class HeadlessTheme
{
    /// <summary>Puts the application into <c>Dark</c> or <c>Light</c>.</summary>
    internal static void UseTheme(string theme) =>
        Application.Current!.RequestedThemeVariant =
            theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    /// <summary>
    /// Resolves a brush from the palette in the CURRENT theme, or <see langword="null"/> when the key is
    /// not there.
    ///
    /// <para>⚠ <see langword="null"/> is a real answer, not a failure to be swallowed: a missing
    /// <c>{DynamicResource}</c> key throws nothing and leaves the property at its default, so a test
    /// that cannot tell "absent" from "present" is a test of nothing.</para>
    /// </summary>
    internal static ISolidColorBrush? Brush(string key)
    {
        var application = Application.Current!;
        return application.TryFindResource(key, application.ActualThemeVariant, out var value) &&
               value is ISolidColorBrush brush
            ? brush
            : null;
    }
}
