using System;
using System.Collections.Generic;
using System.Globalization;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Import;

/// <summary>
/// What became of one value on its way to one column. Shared by <see cref="ImportValueConverter"/> and
/// <see cref="ImportRowValidator"/> so the pipeline's step 3 → step 4 chain is a single currency (§4.4).
/// <para>
/// A struct on purpose: this is allocated once per value per row, and a million-row import must not put a
/// million objects on the heap to say "fine".
/// </para>
/// </summary>
public readonly record struct ImportValueResult
{
    /// <summary>True when <see cref="Value"/> may be written.</summary>
    public bool IsSuccess { get; private init; }

    /// <summary>The converted value, ready for the writer; <c>null</c> means SQL NULL.</summary>
    public object? Value { get; private init; }

    /// <summary>Why the value was refused; <see cref="ImportErrorKind.None"/> on success.</summary>
    public ImportErrorKind Kind { get; private init; }

    /// <summary>True when the value was SHORTENED to fit (only ever with
    /// <see cref="ImportBehaviorOptions.TrimTooLongValues"/> on). The row still succeeds, but it is reported as
    /// a warning carrying the original — §0.2 permits trimming only as an explicit, visible choice.</summary>
    public bool WasTrimmed { get; private init; }

    /// <summary>The value as it appeared in the SOURCE, kept verbatim for the report so the user sees what
    /// they actually have rather than a post-conversion approximation (§0.2 / §0.6).</summary>
    public string? RawText { get; private init; }

    public static ImportValueResult Ok(object? value)
        => new() { IsSuccess = true, Value = value };

    /// <summary>Success, but the value was shortened; <paramref name="original"/> is kept for the report.</summary>
    public static ImportValueResult Trimmed(object? value, string? original)
        => new() { IsSuccess = true, Value = value, WasTrimmed = true, RawText = original };

    public static ImportValueResult Fail(ImportErrorKind kind, string? rawText)
        => new() { IsSuccess = false, Kind = kind, RawText = rawText };

    /// <summary>SQL NULL — the common, successful "this field was empty" answer.</summary>
    public static readonly ImportValueResult Null = Ok(null);
}

/// <summary>
/// ⭐ Turns one raw source value into one CLR value the writer can bind — <b>strictly</b>.
/// <para>
/// This class is where §0.1 lives. It has exactly two allowed outcomes for every input: a value it is
/// <em>certain</em> about, or a refusal naming the reason. It never tries a second interpretation, never falls
/// back to another culture, and never rounds. <c>"1,5"</c> under a <c>.</c> decimal separator is an error, not
/// 15 and not 1.5 — that is the whole point, and it is the difference between an import you can trust and one
/// that quietly rewrites your data.
/// </para>
/// <para>
/// <b>What it deliberately does NOT do</b>, so each question has one owner:
/// <list type="bullet">
/// <item>NOT NULL, length, numeric precision/scale and connection-charset representability →
/// <see cref="ImportRowValidator"/> (pipeline step 4, §4.4). This class answers "what value is this", the
/// validator answers "may this column hold it".</item>
/// <item>Resolving a source-level NULL token (<c>DelimitedOptions.NullToken</c>,
/// <c>ImportBehaviorOptions.TreatEmptyAsNull</c>) → the provider, before a <c>RawRecord</c> is produced. By the
/// time a value arrives here, "this field means NULL" has already been decided once.</item>
/// </list>
/// </para>
/// <para>
/// <b>Integer range is the one exception</b> and it is forced: the converter must produce the column's own CLR
/// width (a <c>short</c> for SMALLINT), so it cannot narrow a value without first proving it fits.
/// </para>
/// </summary>
public static class ImportValueConverter
{
    /// <summary>Converts <paramref name="raw"/> for <paramref name="column"/>. Convenience overload — the
    /// pipeline resolves the type once per column and uses the fast overload below.</summary>
    public static ImportValueResult Convert(object? raw, ColumnSpec column, ImportCultureOptions culture)
        => Convert(raw, ImportTargetType.Resolve(column), culture);

    /// <summary>Converts <paramref name="raw"/> for an already-resolved target type.</summary>
    public static ImportValueResult Convert(object? raw, ImportTargetType type, ImportCultureOptions culture)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (culture is null) throw new ArgumentNullException(nameof(culture));

        if (!type.IsSupported) return ImportValueResult.Fail(ImportErrorKind.UnsupportedTargetType, AsText(raw));

        if (raw is null || raw is DBNull) return ImportValueResult.Null;

        // ⭐ Checked before every target-type branch, on purpose: a cell the source marked as an error must fail
        // for EVERY column kind. Reaching the text branch below would return it as Ok("#N/A") — the one outcome
        // R20 forbids, and the reason SourceErrorValue is a value rather than a convention.
        if (raw is SourceErrorValue sourceError)
            return ImportValueResult.Fail(ImportErrorKind.SourceErrorValue, sourceError.Code);

        if (raw is string text)
        {
            // An empty (or all-whitespace) field is the ABSENCE of a value, not a value to be converted — so
            // it becomes NULL rather than "not a number". For a text column the opposite is true: "" is a
            // legitimate value there and distinct from NULL, so it passes through untouched. (Whether an
            // empty field means NULL at all was already decided upstream by the provider's NULL token; this
            // only keeps a non-text column from reporting a nonsense parse failure.)
            if (!type.IsText && text.Trim().Length == 0) return ImportValueResult.Null;

            return ConvertText(text, type, culture);
        }

        return ConvertNative(raw, type);
    }

    // ── Text ────────────────────────────────────────────────────────────────────────────────────────────

    private static ImportValueResult ConvertText(string text, ImportTargetType type, ImportCultureOptions culture)
        => type.Kind switch
        {
            SqlValueKind.Text or SqlValueKind.TextBlob => ImportValueResult.Ok(text),
            SqlValueKind.Integer => TextToInteger(text, type, culture),
            SqlValueKind.Decimal => TextToDecimal(text, culture),
            SqlValueKind.Float => TextToFloat(text, type, culture),
            SqlValueKind.Boolean => TextToBoolean(text, culture),
            SqlValueKind.Date => TextToDate(text, culture),
            SqlValueKind.Timestamp => TextToTimestamp(text, culture),
            SqlValueKind.Time => TextToTime(text, culture),

            // Text into a binary BLOB would require inventing an encoding or a hex convention. Refused
            // rather than guessed (§0).
            _ => ImportValueResult.Fail(ImportErrorKind.UnsupportedTargetType, text),
        };

    private static ImportValueResult TextToInteger(string text, ImportTargetType type, ImportCultureOptions culture)
    {
        // No AllowDecimalPoint: "12.5" is NOT an integer, whatever the decimal separator is set to.
        if (!long.TryParse(text, IntegerStyles(culture), culture.BuildNumberFormat(), out var value))
            return ImportValueResult.Fail(ImportErrorKind.NotAnInteger, text);

        return NarrowInteger(value, type, text);
    }

    private static ImportValueResult TextToDecimal(string text, ImportCultureOptions culture)
        => decimal.TryParse(text, DecimalStyles(culture), culture.BuildNumberFormat(), out var value)
            ? ImportValueResult.Ok(value)
            : ImportValueResult.Fail(ImportErrorKind.NotANumber, text);

    private static ImportValueResult TextToFloat(string text, ImportTargetType type, ImportCultureOptions culture)
    {
        if (!double.TryParse(text, DecimalStyles(culture), culture.BuildNumberFormat(), out var value))
            return ImportValueResult.Fail(ImportErrorKind.NotANumber, text);

        return NarrowFloat(value, type, text);
    }

    private static ImportValueResult TextToBoolean(string text, ImportCultureOptions culture)
    {
        var token = text.Trim();
        if (culture.IsTrueToken(token)) return ImportValueResult.Ok(true);
        if (culture.IsFalseToken(token)) return ImportValueResult.Ok(false);
        return ImportValueResult.Fail(ImportErrorKind.NotABoolean, text);
    }

    private static ImportValueResult TextToDate(string text, ImportCultureOptions culture)
    {
        // A DATE column has no time part, so a text carrying one does not match a date under the declared
        // settings at all — it is refused as such rather than silently having its time chopped off.
        if (!TryParseExact(text, DateFormats(culture), out var value))
            return ImportValueResult.Fail(ImportErrorKind.NotADateTime, text);

        return ImportValueResult.Ok(value.Date);
    }

    private static ImportValueResult TextToTimestamp(string text, ImportCultureOptions culture)
        => TryParseExact(text, TimestampFormats(culture), out var value)
            ? ImportValueResult.Ok(value)
            : ImportValueResult.Fail(ImportErrorKind.NotADateTime, text);

    private static ImportValueResult TextToTime(string text, ImportCultureOptions culture)
    {
        if (!TimeSpan.TryParseExact(
                text.Trim(), TimeOnlyFormats(culture), CultureInfo.InvariantCulture, TimeSpanStyles.None,
                out var value))
        {
            return ImportValueResult.Fail(ImportErrorKind.NotADateTime, text);
        }

        return ImportValueResult.Ok(value);
    }

    // ── Native (a spreadsheet cell already carries a typed value) ────────────────────────────────────────

    private static ImportValueResult ConvertNative(object raw, ImportTargetType type)
    {
        var text = AsText(raw);

        switch (type.Kind)
        {
            case SqlValueKind.Text or SqlValueKind.TextBlob:
                return ImportValueResult.Ok(text);

            case SqlValueKind.Boolean:
                return raw is bool b
                    ? ImportValueResult.Ok(b)
                    : ImportValueResult.Fail(ImportErrorKind.NotABoolean, text);

            case SqlValueKind.Date:
                if (raw is not DateTime dateValue) return ImportValueResult.Fail(ImportErrorKind.NotADateTime, text);
                // A DATE column keeps no time, so accepting 14:02 here would drop it silently (§0.1).
                return dateValue.TimeOfDay == TimeSpan.Zero
                    ? ImportValueResult.Ok(dateValue.Date)
                    : ImportValueResult.Fail(ImportErrorKind.PrecisionWouldBeLost, text);

            case SqlValueKind.Timestamp:
                return raw is DateTime stamp
                    ? ImportValueResult.Ok(stamp)
                    : ImportValueResult.Fail(ImportErrorKind.NotADateTime, text);

            case SqlValueKind.Time:
                return raw switch
                {
                    TimeSpan span => ImportValueResult.Ok(span),
                    DateTime dt => ImportValueResult.Ok(dt.TimeOfDay),
                    _ => ImportValueResult.Fail(ImportErrorKind.NotADateTime, text),
                };

            case SqlValueKind.BinaryBlob:
                return raw is byte[] bytes
                    ? ImportValueResult.Ok(bytes)
                    : ImportValueResult.Fail(ImportErrorKind.UnsupportedTargetType, text);

            case SqlValueKind.Integer:
                if (!TryNativeToDecimal(raw, out var asInteger))
                    return ImportValueResult.Fail(ImportErrorKind.NotAnInteger, text);
                // 11.5 into an INTEGER column: the value IS a number, so calling it "not an integer" would
                // misdescribe it — what is wrong is that writing it would drop the fraction.
                if (asInteger != decimal.Truncate(asInteger))
                    return ImportValueResult.Fail(ImportErrorKind.PrecisionWouldBeLost, text);
                if (!FitsInt64(asInteger))
                    return ImportValueResult.Fail(ImportErrorKind.ValueOutOfRange, text);
                return NarrowInteger((long)asInteger, type, text);

            case SqlValueKind.Decimal:
                return TryNativeToDecimal(raw, out var asDecimal)
                    ? ImportValueResult.Ok(asDecimal)
                    : ImportValueResult.Fail(ImportErrorKind.NotANumber, text);

            case SqlValueKind.Float:
                return TryNativeToDouble(raw, out var asDouble)
                    ? NarrowFloat(asDouble, type, text)
                    : ImportValueResult.Fail(ImportErrorKind.NotANumber, text);

            default:
                return ImportValueResult.Fail(ImportErrorKind.UnsupportedTargetType, text);
        }
    }

    // ── Narrowing ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Produces the column's OWN integer width, having first proven the value fits it.</summary>
    private static ImportValueResult NarrowInteger(long value, ImportTargetType type, string? rawText)
    {
        var range = type.IntegerRange;
        if (range is null) return ImportValueResult.Fail(ImportErrorKind.UnsupportedTargetType, rawText);

        var (min, max) = range.Value;
        if (value < min || value > max) return ImportValueResult.Fail(ImportErrorKind.ValueOutOfRange, rawText);

        return type.BaseTypeName switch
        {
            "SMALLINT" => ImportValueResult.Ok((short)value),
            "BIGINT" => ImportValueResult.Ok(value),
            _ => ImportValueResult.Ok((int)value),
        };
    }

    /// <summary>
    /// Produces the column's own approximate width. FLOAT is single precision, so a value beyond its range
    /// would become <c>Infinity</c> — silent corruption, and therefore refused. Loss of *significant digits*
    /// is NOT reported: an approximate column is approximate by declaration, and flagging it would make every
    /// FLOAT import an error.
    /// </summary>
    private static ImportValueResult NarrowFloat(double value, ImportTargetType type, string? rawText)
    {
        if (type.BaseTypeName != "FLOAT") return ImportValueResult.Ok(value);

        if (double.IsFinite(value) && Math.Abs(value) > float.MaxValue)
            return ImportValueResult.Fail(ImportErrorKind.ValueOutOfRange, rawText);

        return ImportValueResult.Ok((float)value);
    }

    // ── Parsing plumbing ────────────────────────────────────────────────────────────────────────────────

    private static NumberStyles IntegerStyles(ImportCultureOptions culture)
    {
        var styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
        if (culture.ThousandsSeparator is not null) styles |= NumberStyles.AllowThousands;
        return styles;
    }

    private static NumberStyles DecimalStyles(ImportCultureOptions culture)
        => IntegerStyles(culture) | NumberStyles.AllowDecimalPoint;

    private static bool TryParseExact(string text, string[] formats, out DateTime value)
        => DateTime.TryParseExact(
            text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    /// <summary>
    /// The date shapes accepted under the declared field order. ONE order is tried — that is the whole
    /// contract of <see cref="DateFieldOrder"/>: <c>03.04.2026</c> is 3 April or 4 March depending on this
    /// setting alone, and trying both would be the guess §0.4 forbids.
    /// </summary>
    private static string[] DateFormats(ImportCultureOptions culture)
    {
        if (culture.DateOrder == DateFieldOrder.Iso) return new[] { "yyyy-MM-dd" };

        var sep = Literal(culture.DateSeparator);
        return culture.DateOrder switch
        {
            DateFieldOrder.Mdy => new[] { $"M{sep}d{sep}yyyy", $"M{sep}d{sep}yy" },
            DateFieldOrder.Ymd => new[] { $"yyyy{sep}M{sep}d" },
            _ => new[] { $"d{sep}M{sep}yyyy", $"d{sep}M{sep}yy" },
        };
    }

    /// <summary>Date-and-time shapes: every date form alone (⇒ midnight) and every date form followed by a
    /// time, separated by a space or an ISO <c>T</c>.</summary>
    private static string[] TimestampFormats(ImportCultureOptions culture)
    {
        var dates = DateFormats(culture);
        var times = TimeOfDayFormats(culture);

        var formats = new List<string>(dates.Length * (1 + times.Length * 2));
        foreach (var date in dates)
        {
            formats.Add(date);
            foreach (var time in times)
            {
                formats.Add(date + " " + time);
                formats.Add(date + "'T'" + time);
            }
        }
        return formats.ToArray();
    }

    private static string[] TimeOfDayFormats(ImportCultureOptions culture)
    {
        var sep = Literal(culture.TimeSeparator);
        return new[] { $"H{sep}mm{sep}ss.FFFFFFF", $"H{sep}mm{sep}ss", $"H{sep}mm" };
    }

    /// <summary><see cref="TimeSpan"/> patterns for a bare TIME column. Distinct from
    /// <see cref="TimeOfDayFormats"/> because <c>TimeSpan</c> custom formats use <c>hh</c>/<c>mm</c>/<c>ss</c>
    /// and require every component to be present.</summary>
    private static string[] TimeOnlyFormats(ImportCultureOptions culture)
    {
        var sep = Literal(culture.TimeSeparator);
        return new[] { $"hh{sep}mm{sep}ss\\.FFFFFFF", $"hh{sep}mm{sep}ss", $"hh{sep}mm", $"h{sep}mm{sep}ss", $"h{sep}mm" };
    }

    /// <summary>Wraps a separator so it is read as itself. Necessary because <c>:</c> and <c>/</c> are
    /// pattern-significant in a .NET date format and would otherwise mean "whatever the culture's separator
    /// is" rather than the character the user declared.</summary>
    private static string Literal(char separator)
        => separator == '\'' ? "\\'" : "'" + separator + "'";

    // ── Native numeric plumbing ─────────────────────────────────────────────────────────────────────────

    private static bool TryNativeToDecimal(object raw, out decimal value)
    {
        value = 0m;
        try
        {
            switch (raw)
            {
                case decimal d: value = d; return true;
                case double dbl:
                    if (!double.IsFinite(dbl)) return false;
                    value = (decimal)dbl;
                    return true;
                case float f:
                    if (!float.IsFinite(f)) return false;
                    value = (decimal)f;
                    return true;
                case byte or sbyte or short or ushort or int or uint or long:
                    value = System.Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
                    return true;
                case ulong u:
                    value = u;
                    return true;
                default:
                    return false;
            }
        }
        catch (OverflowException)
        {
            // A double too large for decimal. Not representable ⇒ not converted.
            return false;
        }
    }

    private static bool TryNativeToDouble(object raw, out double value)
    {
        value = 0d;
        switch (raw)
        {
            case double d: value = d; return true;
            case float f: value = f; return true;
            case decimal m: value = (double)m; return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                value = System.Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return true;
            default:
                return false;
        }
    }

    private static bool FitsInt64(decimal value) => value >= long.MinValue && value <= long.MaxValue;

    /// <summary>
    /// Renders a raw value as text — for the error report, and for a native value bound for a text column.
    /// <para>
    /// Always InvariantCulture, and ISO for dates: this text can end up IN the database (a spreadsheet's
    /// numeric code column mapped to VARCHAR), so it must not depend on the machine's regional settings. The
    /// same discipline <c>SqlLiteralWriter</c> applies in the other direction.
    /// </para>
    /// </summary>
    internal static string? AsText(object? raw) => raw switch
    {
        null or DBNull => null,
        string s => s,
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
        bool b => b ? "TRUE" : "FALSE",
        byte[] bytes => "0x" + System.Convert.ToHexString(bytes),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => raw.ToString(),
    };
}
