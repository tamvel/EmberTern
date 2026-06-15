using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EmberTern.App;

/// <summary>
/// Resolves an icon-geometry resource key (e.g. <c>"Icon.Table"</c>) into the shared
/// <see cref="Geometry"/> defined in <c>Themes/IconGeometries.axaml</c>. Geometries are
/// theme-INVARIANT (shape, not color), so this is a plain single-value converter looked
/// up without a theme variant; color flows separately through
/// <see cref="IconBrushConverter"/> on the icon's Foreground. Mirrors the IconBrushConverter
/// pattern so a VM can keep a key string ("no Avalonia types in the VM") while the view
/// renders an <see cref="Controls.SvgIcon"/>.
/// </summary>
public sealed class IconGeometryConverter : IValueConverter
{
    public static readonly IconGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || key.Length == 0)
        {
            return null;
        }

        var app = Application.Current;
        return app is not null && app.Resources.TryGetResource(key, null, out var found) && found is Geometry geometry
            ? geometry
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
