using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace EmberTern.App.Converters;

/// <summary>
/// <c>true</c> → the star width given as the parameter (e.g. <c>3*</c>); <c>false</c> → zero.
///
/// <para>⭐ Why this exists at all — Product Polish M2b, step 10, and it is the third time this project has
/// paid for the same fact: <b>a collapsed child does NOT collapse the space its container reserved for it.</b>
/// Data Import's type grid declares a <c>Basis</c> column at <c>3*</c>, shown only when the import has
/// per-column basis text. With it hidden the <c>TextBlock</c> disappears but the COLUMN keeps measuring
/// <c>3*</c> — so roughly a third of the grid sat empty and the columns the user actually reads (Column, Type)
/// were squeezed into what was left. The user reported it as "Column has too little room", which is true, but
/// the cause was next door.</para>
///
/// <para>⚠ The same shape removed the titlebar brand block's leftover inset during the branding sprint (a
/// container whose children are all collapsed is still measured) — the difference is that a
/// <c>ColumnDefinition</c> cannot be given <c>IsVisible</c>, so the width itself has to react.</para>
///
/// <para>⚠ It lives in the VIEW layer on purpose: <see cref="GridLength"/> is an Avalonia type, and
/// architecture rule #1 keeps those out of view models. The view model exposes a <c>bool</c>; turning it into
/// a width is presentation.</para>
/// </summary>
public sealed class BoolToStarWidthConverter : IValueConverter
{
    public static readonly BoolToStarWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool visible || !visible)
            return new GridLength(0, GridUnitType.Pixel);

        var stars = 1d;
        if (parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            stars = parsed;

        return new GridLength(stars, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("BoolToStarWidthConverter is one-way — a width never sets a flag.");
}
