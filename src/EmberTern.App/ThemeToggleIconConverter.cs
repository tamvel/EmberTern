using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace EmberTern.App;

/// <summary>
/// Maps the active <see cref="ThemeVariant"/> to the theme-toggle glyph representing the ACTION on
/// click: Dark active → Sun (click switches to light), Light active → Moon (click switches to dark).
/// Bound to the window's <c>ActualThemeVariant</c> so the icon updates immediately when the theme
/// changes. Resolves the shared geometry the same way as <see cref="IconGeometryConverter"/>.
/// </summary>
public sealed class ThemeToggleIconConverter : IValueConverter
{
    public static readonly ThemeToggleIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is ThemeVariant v && v == ThemeVariant.Dark ? "Icon.Sun" : "Icon.Moon";
        var app = Application.Current;
        return app is not null && app.Resources.TryGetResource(key, null, out var found) && found is Geometry geometry
            ? geometry
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
