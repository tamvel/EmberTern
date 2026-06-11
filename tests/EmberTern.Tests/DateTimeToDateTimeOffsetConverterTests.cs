using System;
using System.Globalization;
using Avalonia.Data;
using EmberTern.App.Converters;
using Xunit;

namespace EmberTern.Tests;

public class DateTimeToDateTimeOffsetConverterTests
{
    private static object? Convert(object? value)
        => DateTimeToDateTimeOffsetConverter.Instance.Convert(value, typeof(DateTimeOffset?), null, CultureInfo.InvariantCulture);

    private static object? ConvertBack(object? value)
        => DateTimeToDateTimeOffsetConverter.Instance.ConvertBack(value, typeof(DateTime?), null, CultureInfo.InvariantCulture);

    [Fact]
    public void Convert_Null_ReturnsNull()
    {
        Assert.Null(Convert(null));
    }

    [Fact]
    public void Convert_UnspecifiedDateTime_SurfaceAsLocalDateTimeOffset()
    {
        // Firebird's managed driver returns DateTime values with Kind=Unspecified.
        // Treating them as UTC would shift the displayed wall-clock; treating as
        // Local matches IBExpert + DataGrip behavior.
        var unspec = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Unspecified);
        var result = Convert(unspec);
        var dto = Assert.IsType<DateTimeOffset>(result);
        Assert.Equal(unspec.Date, dto.DateTime.Date);
    }

    [Fact]
    public void Convert_DateTimeOffset_PassesThrough()
    {
        var dto = new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.FromHours(2));
        var result = Convert(dto);
        Assert.Equal(dto, result);
    }

    [Fact]
    public void Convert_UnknownType_ReturnsDoNothing()
    {
        Assert.Same(BindingOperations.DoNothing, Convert("not a date"));
    }

    [Fact]
    public void ConvertBack_DateTimeOffset_StripsToDateTime()
    {
        var dto = new DateTimeOffset(2026, 6, 11, 12, 30, 45, TimeSpan.FromHours(2));
        var result = ConvertBack(dto);
        Assert.IsType<DateTime>(result);
        Assert.Equal(dto.DateTime, (DateTime)result!);
    }

    [Fact]
    public void ConvertBack_Null_ReturnsNull()
    {
        Assert.Null(ConvertBack(null));
    }
}
