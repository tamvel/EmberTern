using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EmberTern.App.Converters;

// Renders nullable numeric values as "" when zero / null, and as the value's
// string form otherwise. Used for the Skala (Scale) column in TableDetailTabView
// so INTEGER columns (scale = 0) read as blank instead of a noisy "0".
public sealed class ZeroToEmptyConverter : IValueConverter
{
    public static readonly ZeroToEmptyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        return value switch
        {
            int i => i == 0 ? string.Empty : i.ToString(culture),
            long l => l == 0L ? string.Empty : l.ToString(culture),
            short s => s == 0 ? string.Empty : s.ToString(culture),
            double d => d == 0d ? string.Empty : d.ToString(culture),
            decimal m => m == 0m ? string.Empty : m.ToString(culture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
