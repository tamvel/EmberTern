using System;
using System.Globalization;

namespace EmberTern.Core.Export;

/// <summary>
/// Turns a raw cell value (<c>object?</c> from the reader) into its export string. Shared by every
/// text exporter so number/date/NULL/BLOB handling is identical:
/// <list type="bullet">
/// <item>NULL / <c>DBNull</c> → empty field.</item>
/// <item>Binary BLOB (<c>byte[]</c>) → a <c>(BLOB)</c> placeholder — base64 would bloat the file and
/// Excel wouldn't use it; text BLOBs already arrive decoded as <c>string</c>.</item>
/// <item><see cref="IFormattable"/> (numbers, dates, decimals) → formatted with the chosen culture,
/// so Excel-in-pl-PL can get <c>,</c> decimals (Current) while a machine consumer gets Invariant.</item>
/// <item>everything else (bool, string) → <see cref="object.ToString"/>.</item>
/// </list>
/// </summary>
public static class ExportValueFormatter
{
    public const string BlobPlaceholder = "(BLOB)";

    public static string Format(object? value, CultureInfo culture) => value switch
    {
        null or DBNull => string.Empty,
        byte[] => BlobPlaceholder,
        IFormattable f => f.ToString(null, culture),
        _ => value.ToString() ?? string.Empty,
    };
}
