using System;
using System.Globalization;
using Avalonia.Data;
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

    // One-way only. Avalonia 12's DataGridTextColumn wires its binding as TwoWay
    // even when the grid is IsReadOnly=True (the binding is shared between the
    // display TextBlock and the editor TextBox); throwing here gets caught by
    // the binding pipeline but trips Visual Studio's "break on user-unhandled
    // exception" prompt. DoNothing tells the binding layer to skip the write.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
