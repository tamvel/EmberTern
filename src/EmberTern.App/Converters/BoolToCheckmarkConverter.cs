using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EmberTern.App.Converters;

// Renders bool as "✓" / "" for read-only display in DataGrid cells. Sidesteps
// FluentTheme's CheckBox template that doesn't scale cleanly below ~20 px —
// at 14×14 the inner border element renders as a tan rectangle instead of a
// proper box. The text glyph aligns naturally with surrounding cell text and
// inherits the row's selection/hover colors.
public sealed class BoolToCheckmarkConverter : IValueConverter
{
    public static readonly BoolToCheckmarkConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "✓" : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
