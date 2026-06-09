using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace EmberTern.App.Converters;

// Renders a bool IsDescending flag as the localized "Ascending" / "Descending"
// label for the constraint grids' Sort column. Strings come from UiStrings so
// they remain centralized and translatable.
public sealed class BoolToSortOrderConverter : IValueConverter
{
    public static readonly BoolToSortOrderConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? UiStrings.TableDetailConstraintSortDescending
            : UiStrings.TableDetailConstraintSortAscending;

    // One-way only — see BoolToCheckmarkConverter for the rationale.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
