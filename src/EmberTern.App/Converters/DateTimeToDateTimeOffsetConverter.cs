using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace EmberTern.App.Converters;

/// <summary>
/// Bridges the gap between Firebird's DateTime values (returned by the
/// FirebirdSql managed driver as <see cref="DateTime"/>) and Avalonia's
/// <c>CalendarDatePicker</c>, whose <c>SelectedDate</c> is a
/// <see cref="DateTimeOffset"/>?. Unspecified DateTime values are surfaced
/// as Local kind so the picker shows them at the user's wall-clock time
/// rather than treating them as UTC.
/// </summary>
public sealed class DateTimeToDateTimeOffsetConverter : IValueConverter
{
    public static readonly DateTimeToDateTimeOffsetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            null => null,
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dt, DateTimeKind.Local)
                : dt),
            _ => BindingOperations.DoNothing,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            null => null,
            DateTimeOffset dto => dto.DateTime,
            DateTime dt => dt,
            _ => BindingOperations.DoNothing,
        };
}
