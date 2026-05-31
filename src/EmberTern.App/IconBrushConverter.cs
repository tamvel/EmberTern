using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace EmberTern.App;

/// <summary>
/// Resolves a theme-dictionary key (e.g. <c>"IconColor_Table"</c>) into the current
/// <see cref="IBrush"/> for that key. Driven by a <see cref="Avalonia.Data.MultiBinding"/>
/// whose second source is <see cref="Application.ActualThemeVariant"/>, so a theme
/// toggle re-fires the converter and the brush refreshes live without rebuilding the tree.
/// </summary>
public sealed class IconBrushConverter : IMultiValueConverter
{
    public static readonly IconBrushConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0 || values[0] is not string key || key.Length == 0)
        {
            return AvaloniaProperty.UnsetValue;
        }

        var app = Application.Current;
        if (app is null)
        {
            return AvaloniaProperty.UnsetValue;
        }

        // values[1] is ActualThemeVariant — read it from the binding rather than from
        // app.ActualThemeVariant directly so changes propagate through the binding pipeline.
        var theme = values.Count > 1 && values[1] is ThemeVariant v ? v : app.ActualThemeVariant;

        return app.Resources.TryGetResource(key, theme, out var found) && found is IBrush brush
            ? brush
            : AvaloniaProperty.UnsetValue;
    }
}
