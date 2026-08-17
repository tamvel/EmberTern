using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EmberTern.LicenseManager;

/// <summary>
/// Turns an icon KEY into the geometry it names.
///
/// <para>⭐ This is what lets a view model carry <c>"Icon.AlertTriangle"</c> — a string — instead of a
/// <see cref="Geometry"/>, which would put an Avalonia type in a view model (Architecture rule 1). It is
/// the same shape as EmberTern's own <c>IconGeometryConverter</c> and its <c>IconResourceKey</c>
/// pattern.</para>
///
/// <para>⚠ An unknown key yields <see langword="null"/> rather than throwing: a missing icon must not be
/// able to take down a window whose actual job is to show the operator a message. The absence is visible
/// — and <c>IconGeometriesSplitTests</c> in EmberTern is what makes it noticed rather than lived with.</para>
/// </summary>
public sealed class IconGeometryConverter : IValueConverter
{
    /// <summary>The one instance, referenced from XAML with <c>{x:Static}</c>.</summary>
    public static readonly IconGeometryConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key &&
        Application.Current is { } application &&
        application.Resources.TryGetResource(key, null, out var found) &&
        found is Geometry geometry
            ? geometry
            : null;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}
