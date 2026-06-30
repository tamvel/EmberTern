using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace EmberTern.App.Converters;

/// <summary>Maps a privilege cell state (0 none, 1 granted, 2 granted WITH GRANT
/// OPTION) to a compact glyph: "" / "✓" / "✓+". The "+" marks the grant option
/// (the grantee can pass the privilege on).</summary>
public sealed class PrivilegeStateGlyphConverter : IValueConverter
{
    public static readonly PrivilegeStateGlyphConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int s ? s switch { 1 => "✓", 2 => "✓+", _ => string.Empty } : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

/// <summary>Maps a privilege cell state + the active theme to a brush: transparent
/// (none), success/green (granted), accent/blue (granted with grant option). Driven by
/// a MultiBinding whose second value is <c>ActualThemeVariant</c> so it refreshes on
/// theme toggle (same shape as <see cref="IconBrushConverter"/>).</summary>
public sealed class PrivilegeStateBrushConverter : IMultiValueConverter
{
    public static readonly PrivilegeStateBrushConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = values.Count > 0 && values[0] is int i ? i : 0;
        var key = state switch { 1 => "SuccessIconBrush", 2 => "AccentBrush", _ => null };
        if (key is null) return Brushes.Transparent;

        var app = Application.Current;
        if (app is null) return Brushes.Transparent;
        var theme = values.Count > 1 && values[1] is ThemeVariant v ? v : app.ActualThemeVariant;
        return app.Resources.TryGetResource(key, theme, out var found) && found is IBrush brush
            ? brush
            : Brushes.Transparent;
    }
}
