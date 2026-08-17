using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace EmberTern.LicenseManager;

/// <summary>
/// Maps the active theme to the toggle glyph representing the ACTION on click: Dark active → Sun (click
/// switches to Light), Light active → Moon.
///
/// <para>⭐ The geometries are EmberTern's own, linked rather than copied. ⚠ The twenty lines of the
/// converter itself could not come across: <c>EmberTern.App.ThemeToggleIconConverter</c> lives in an
/// assembly this project deliberately does not reference, and adding that reference to save twenty lines
/// would drag the whole client application — and its Firebird driver — into the issuer's solution.</para>
///
/// <para>⚠ Bound to the window's <c>ActualThemeVariant</c> rather than set on click, so the glyph is
/// correct however the theme changed — including at start-up, before anybody has clicked anything.</para>
/// </summary>
public sealed class ThemeToggleIconConverter : IValueConverter
{
    /// <summary>The one instance, referenced from XAML with <c>{x:Static}</c>.</summary>
    public static readonly ThemeToggleIconConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is ThemeVariant variant && variant == ThemeVariant.Dark ? "Icon.Sun" : "Icon.Moon";

        return Application.Current is { } application &&
               application.Resources.TryGetResource(key, null, out var found) &&
               found is Geometry geometry
            ? geometry
            : null;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}
