using System;
using System.Globalization;

namespace EmberTern.Core.Formatting;

/// <summary>
/// ⭐ <b>The one place a date or a time becomes text FOR A HUMAN.</b> The default follows
/// <see cref="CultureInfo.CurrentCulture"/> — including the user's own Windows short/long-date overrides —
/// and nothing here is ever parsed back or sent anywhere.
/// <para>
/// ⚠ <b>Two documented departures from the reader's culture, and both are still about the reader.</b>
/// <see cref="LogTime"/> is fixed-width because its callers are log COLUMNS; the <c>Firebird*</c> family is
/// the ENGINE's own form because its caller is the debugger, where the value is being compared against
/// <c>isql</c>, against the stepped source, and against literals about to be typed into a Watch. Each carries
/// its reasoning in place.
/// </para>
/// <para>
/// ⛔ <b>The line this class draws is the whole point, and it runs the other way for machines.</b> A value on
/// its way to Firebird, into generated DDL, into a <c>.sql</c> script, into an export, into a settings file
/// or into a log filename must stay <see cref="CultureInfo.InvariantCulture"/> — those are contracts with a
/// parser, not with a reader, and a culture-dependent separator there is a data defect (a Polish
/// <c>07.08.2026</c> in a <c>Copy as INSERT</c> statement is not a formatting preference, it is a broken
/// statement). Those paths deliberately do NOT call this class; <c>DatePresentationTests</c> keeps the
/// boundary honest in both directions.
/// </para>
/// <para>
/// ⚠ <b>What the 2026-08-07 audit actually measured, because it contradicts the obvious guess.</b> The data
/// grids were never tied to the invariant culture: the SQL results grid renders through a binding and the
/// Table Data grid through <c>object.ToString()</c>, and BOTH resolve to <c>CurrentCulture</c> — verified
/// live to reproduce this machine's Windows override (<c>pl-PL</c> with a short date of <c>yyyy-MM-dd</c>,
/// which is where the "rigid" ISO look came from, and it is the user's own setting). What WAS hard-coded were
/// smaller, quieter surfaces: the About window's release date (<c>d MMMM yyyy</c> under the invariant
/// culture, so an English month name on every machine) and the parameter-history label.
/// </para>
/// <para>
/// ⭐ <b>Why this exists rather than a <c>CultureInfo.CurrentCulture</c> at each call site.</b> The user's
/// stated direction is: system culture now, a chosen format in Settings Center later. A preference needs
/// exactly one place to take effect; scattering the culture would make that later step a search-and-replace
/// across the application, which is how a "the setting works everywhere except one screen" defect is born.
/// This is a seam, not a feature — there is no format option here today.
/// </para>
/// </summary>
public static class DateTimeDisplay
{
    /// <summary>The date alone, in the reader's own short-date format (respects Windows overrides).</summary>
    public static string Date(DateTime value) => value.ToString("d", CultureInfo.CurrentCulture);

    /// <summary>The date alone, spelled out — for a single prominent date such as a release date.</summary>
    public static string LongDate(DateTime value) => value.ToString("D", CultureInfo.CurrentCulture);

    /// <summary>Date and time, short forms of both.</summary>
    public static string DateAndTime(DateTime value) => value.ToString("g", CultureInfo.CurrentCulture);

    /// <summary>Date and time down to the second — the culture's short date plus its LONG time.</summary>
    public static string DateAndTimeWithSeconds(DateTime value) => value.ToString("G", CultureInfo.CurrentCulture);

    /// <summary>A time of day in the reader's own long-time format.</summary>
    public static string Time(TimeSpan value) => TimeOnly.FromTimeSpan(value).ToString("T", CultureInfo.CurrentCulture);

    /// <summary>
    /// A clock time, always 24-hour with fixed-width fields.
    /// <para>
    /// ⚠ Deliberately NOT the culture's time pattern, and this is the one exception in the class. Its callers
    /// are LOG COLUMNS — the Messages log, the Trace monitor, the debugger's Executed-SQL audit — where the
    /// stamps are read down a column and compared to each other, so a constant width matters more than local
    /// habit, and a 12-hour culture would make consecutive entries change length. It is a reading aid, not a
    /// date the user is being told.
    /// </para>
    /// </summary>
    public static string LogTime(DateTime value, bool withMilliseconds = false)
        => value.ToString(withMilliseconds ? "HH:mm:ss.fff" : "HH:mm:ss", CultureInfo.InvariantCulture);

    // ── The engine's own form: a value the user is reading AS FIREBIRD SEES IT ────────────────────────────
    //
    // ⭐ A third category, and it is neither of the two above. The debugger shows values that live inside a
    // running Firebird routine, so its reader is comparing them against `isql`, against the source they are
    // stepping through, and against literals they are about to type into a Watch. There the engine's canonical
    // form IS the readable one, and a culture-shaped `08/07/2026` (the invariant "G" the panel used to emit)
    // reads as an American date on a Polish machine while matching nothing the engine ever prints.
    //
    // ⚠ This is a DISPLAY decision, not the machine boundary the class header describes: nothing here is
    // parsed by Firebird. It sits in this class rather than in the debugger so the shape has one owner —
    // the panel, the inline annotations, the data tips and the inline-edit box must all agree, and the edit
    // box in particular must round-trip (`DebuggerTabViewModel.TryParseEditedValue` reads it back under the
    // invariant culture, which accepts exactly this form).

    /// <summary>Firebird's canonical date form, <c>yyyy-MM-dd</c>.</summary>
    public static string FirebirdDate(DateTime value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Firebird's canonical timestamp form, <c>yyyy-MM-dd HH:mm:ss</c> — with the fractional part appended
    /// only when it is non-zero.
    /// <para>⚠ The fraction is not cosmetic: this same text seeds the debugger's inline value editor, so
    /// dropping a sub-second component would silently truncate the value the user then commits.</para>
    /// </summary>
    public static string FirebirdTimestamp(DateTime value)
        => value.Ticks % TimeSpan.TicksPerSecond == 0
            ? value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : value.ToString("yyyy-MM-dd HH:mm:ss.FFFF", CultureInfo.InvariantCulture);

    /// <summary>
    /// Firebird's canonical timestamp form truncated to the SECOND — what a hand-editing surface seeds its
    /// box with.
    /// <para>
    /// ⚠ <b>Separate from <see cref="FirebirdTimestamp"/> on the user's explicit call (2026-08-08), and the
    /// split is the decision.</b> Sub-second digits are needed very rarely and make a value tedious to retype,
    /// so the data grid's editor works to the second; the debugger keeps full precision, because there the
    /// value is being INSPECTED rather than retyped and a hidden fraction would misrepresent frame state.
    /// ⛔ Do not merge the two — each surface's answer is right for its own job.
    /// </para>
    /// </summary>
    public static string FirebirdTimestampToSecond(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Firebird's canonical time form, <c>HH:mm:ss</c> (fraction only when non-zero).</summary>
    public static string FirebirdTime(TimeSpan value)
        => value.Ticks % TimeSpan.TicksPerSecond == 0
            ? value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"hh\:mm\:ss\.FFFF", CultureInfo.InvariantCulture);

    /// <summary>
    /// The engine form of a value, or <c>null</c> when the value is not a date/time kind at all.
    /// <para>
    /// ⭐ <b>Returning <c>null</c> for "not mine" is the seam.</b> The debugger deliberately renders numbers
    /// under the invariant culture (it is the harness's own literal convention), so this must answer only for
    /// the kinds it owns and leave every other decision where it already lives.
    /// </para>
    /// <para>
    /// ⚠ <paramref name="declaredType"/> is what separates a <c>DATE</c> from a <c>TIMESTAMP</c> standing at
    /// midnight — the driver hands both back as a <see cref="DateTime"/>, so without the declared type a
    /// midnight timestamp would silently lose its <c>00:00:00</c>. When no type is known (a Watch on an
    /// arbitrary expression) the value itself is the only available evidence, and it is used as such.
    /// </para>
    /// </summary>
    public static string? FirebirdValue(object? value, string? declaredType = null) => value switch
    {
        DateTime dt => IsDateOnly(declaredType, dt) ? FirebirdDate(dt) : FirebirdTimestamp(dt),
        DateTimeOffset dto => FirebirdValue(dto.LocalDateTime, declaredType),
        DateOnly d => FirebirdDate(d.ToDateTime(TimeOnly.MinValue)),
        TimeSpan ts => FirebirdTime(ts),
        TimeOnly t => FirebirdTime(t.ToTimeSpan()),
        _ => null,
    };

    private static bool IsDateOnly(string? declaredType, DateTime value)
    {
        var type = declaredType?.TrimStart();
        if (!string.IsNullOrEmpty(type))
        {
            // ⚠ TIMESTAMP first: "DATE" is a prefix of nothing else here, but reading the more specific name
            // first keeps the pair readable and survives a future "DATETIME"-shaped spelling.
            if (type.StartsWith("TIMESTAMP", StringComparison.OrdinalIgnoreCase)) return false;
            if (type.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return value.TimeOfDay == TimeSpan.Zero;
    }

    // ── A grid cell: the FIREBIRD type decides which parts are shown ──────────────────────────────────────

    /// <summary>What a Firebird column type means for display. Deliberately about the DECLARED type, never
    /// about the CLR type of the value.</summary>
    private enum FirebirdTemporalKind { None, Date, Time, Timestamp }

    /// <summary>
    /// Renders a data-grid cell whose column type is known, or <c>null</c> when the type is not a temporal one
    /// (and the caller should fall back to the value's own <c>ToString()</c>).
    /// <para>
    /// ⛔⛔ <b>The decision is the COLUMN's Firebird type, never the value's CLR type — reported 2026-08-08 and
    /// it is the whole point of this method.</b> A Firebird <c>DATE</c> and a <c>TIMESTAMP</c> both arrive as a
    /// <see cref="DateTime"/>, so a renderer that asks the value what it is has no way to tell them apart: it
    /// either prints <c>00:00:00</c> on a column that stores no time at all (the reported defect — an invented
    /// time the user cannot edit away), or it guesses from midnight and then silently hides a real
    /// <c>00:00:00</c> on a TIMESTAMP. Only the declared type answers both.
    /// </para>
    /// <para>
    /// ⚠ <c>WITH TIME ZONE</c> is deliberately NOT handled here: its value is not a <see cref="DateTime"/> but
    /// the driver's own zoned type, which already prints the offset. Returning <c>null</c> keeps that value
    /// whole rather than reformatting it through a type that cannot carry a zone.
    /// </para>
    /// </summary>
    public static string? CellForType(object? value, string? firebirdType)
    {
        if (value is null) return null;
        return KindOf(firebirdType) switch
        {
            FirebirdTemporalKind.Date when value is DateTime d => Date(d),
            FirebirdTemporalKind.Time when value is TimeSpan ts => Time(ts),
            FirebirdTemporalKind.Time when value is DateTime d => Time(d.TimeOfDay),
            FirebirdTemporalKind.Timestamp when value is DateTime d => DateAndTimeWithSeconds(d),
            _ => null,
        };
    }

    private static FirebirdTemporalKind KindOf(string? firebirdType)
    {
        var type = firebirdType?.Trim();
        if (string.IsNullOrEmpty(type)) return FirebirdTemporalKind.None;
        // ⚠ A zoned type keeps its own rendering (above), so it must fall out BEFORE the prefix tests —
        // "TIMESTAMP WITH TIME ZONE" starts with "TIMESTAMP".
        if (type.Contains("TIME ZONE", StringComparison.OrdinalIgnoreCase)) return FirebirdTemporalKind.None;
        if (type.StartsWith("TIMESTAMP", StringComparison.OrdinalIgnoreCase)) return FirebirdTemporalKind.Timestamp;
        if (type.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)) return FirebirdTemporalKind.Date;
        if (type.StartsWith("TIME", StringComparison.OrdinalIgnoreCase)) return FirebirdTemporalKind.Time;
        return FirebirdTemporalKind.None;
    }
}
