using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace EmberTern.Core.Export.Sql;

/// <summary>Why a value could not be turned into an SQL literal. Never a message — App maps this to
/// <c>UiStrings</c> (rule #1: Core has no UI strings; rule #6: no <c>.resx</c>).</summary>
public enum SqlLiteralRefusal
{
    /// <summary>Not a refusal — a literal was written.</summary>
    None = 0,

    /// <summary>The column's declared type has no <see cref="SqlValueKind"/> mapping (ARRAY, DECFLOAT,
    /// INT128, <c>WITH TIME ZONE</c>, or an unmapped kind). Nothing is guessed.</summary>
    UnsupportedKind,

    /// <summary>The CLR value is not one this <see cref="SqlValueKind"/> can render — a mapping bug or a
    /// driver surprise. Refusing beats <c>ToString()</c>-ing an unknown shape into a statement.</summary>
    UnexpectedValueType,

    /// <summary>The value is of the right shape but no faithful Firebird literal of this kind exists for
    /// it (a DATE carrying a time part, a TIME outside 00:00–24:00, a temporal with precision finer than
    /// Firebird's 0.1 ms, a non-finite float). Rendering it anyway would lose or invent data.</summary>
    NotRepresentable,

    /// <summary>The literal would exceed what a single Firebird literal can carry. Refused rather than
    /// truncated — a truncated literal is silent data corruption (§0 / rule #11).</summary>
    TooLarge,
}

/// <summary>
/// The size ceilings <see cref="SqlLiteralWriter"/> refuses above — <b>measured against a live Firebird
/// 5.0 engine, not derived from the documented type maxima</b> (which give the wrong number for both).
/// They are settable so a caller may impose a <em>stricter</em> policy; a looser one only moves the
/// failure from EmberTern to the engine.
/// <para>
/// <b>These are necessary, not sufficient — and that is a measured fact, not a caveat.</b> The real
/// constraint is on the <em>statement</em>: Firebird's DSQL text limit is ~65,535 characters, and the
/// largest hex literal shrinks by exactly the amount of surrounding statement text (32,752 bytes alone;
/// 30,749 with 4 KB of other text). So no per-value constant can be sufficient — two 20 KB blobs each
/// pass every check here and fail as one statement. A per-value ceiling can only refuse what could
/// <em>never</em> fit; refusing what does not fit <em>this</em> statement belongs to the statement
/// builder, which is the only thing that knows the assembled length.
/// </para>
/// <para>Only the BLOB kinds carry a ceiling. A CHAR/VARCHAR value cannot exceed one <em>by
/// construction</em> — its column type's maximum and the literal ceiling are the same number, under
/// every charset.</para>
/// </summary>
public sealed record SqlLiteralLimits
{
    /// <summary>The engine's own ceilings — the default for every caller.</summary>
    public static readonly SqlLiteralLimits Default = new();

    /// <summary>Largest <see cref="SqlValueKind.BinaryBlob"/> rendered as an <c>x'…'</c> hex literal.
    /// Measured: 32,752 bytes (65,504 hex digits) is the most that fits in an otherwise-minimal
    /// statement — i.e. this is the "could never fit" bound, since each byte costs two characters of the
    /// ~65,535-character statement budget.</summary>
    public int MaxBinaryBytes { get; init; } = 32752;

    /// <summary>Largest <see cref="SqlValueKind.TextBlob"/> rendered as a quoted literal, in
    /// <b>characters</b>.
    /// <para>
    /// Characters, not bytes, because the limit measured that way: on a UTF8 connection both an ASCII
    /// string and a two-byte-per-char string cap at the same <b>8,191 characters</b> — Firebird reserves
    /// 4 bytes per UTF8 character, so 8,191 × 4 ≈ the 32,765-byte string ceiling regardless of the actual
    /// content. A UTF-8 <em>byte</em> count would therefore have been the wrong unit entirely.
    /// </para>
    /// <para>
    /// The ceiling is <b>charset-dependent</b> — measured 8,191 characters on UTF8 versus 32,767 on
    /// WIN1250 — and Core does not know the connection charset. 8,191 is the worst case, so it can only
    /// refuse early, never emit an over-long literal. Note this over-refuses by ~4× on a WIN1250
    /// connection (the lab's, and the user's real one); making it charset-aware means threading the
    /// connection charset in, which is worth doing only if a real text blob ever hits it.
    /// </para></summary>
    public int MaxTextBlobChars { get; init; } = 8191;
}

/// <summary>The outcome of <see cref="SqlLiteralWriter.Write(object?, SqlValueKind)"/>: either a
/// literal, or a structured refusal. There is no third state and no "best effort" — a caller that gets
/// a refusal must not emit a statement for this row.</summary>
public readonly record struct SqlLiteralResult
{
    private SqlLiteralResult(string? literal, SqlLiteralRefusal refusal)
    {
        Literal = literal;
        Refusal = refusal;
    }

    /// <summary>The SQL literal, non-null exactly when <see cref="IsWritten"/>.</summary>
    public string? Literal { get; }

    public SqlLiteralRefusal Refusal { get; }

    [MemberNotNullWhen(true, nameof(Literal))]
    public bool IsWritten => Refusal == SqlLiteralRefusal.None;

    public static SqlLiteralResult Written(string literal) => new(literal, SqlLiteralRefusal.None);

    public static SqlLiteralResult Refused(SqlLiteralRefusal reason) => new(null, reason);
}

/// <summary>
/// <b>The one place a value becomes SQL.</b> Renders a raw cell value (the <c>object?</c> the driver
/// returned) as a Firebird literal, driven by the column's declared <see cref="SqlValueKind"/> — never
/// by the value's CLR type (DATE and TIMESTAMP are both <see cref="DateTime"/>).
/// <para>
/// Pure, no culture state, no DB, no Avalonia. Every rule below comes from a probe against a live
/// Firebird 5.0 engine, not from inference — the four rendering mistakes it exists to make impossible
/// are each a measured failure:
/// <list type="number">
/// <item><b>Culture.</b> Always <see cref="CultureInfo.InvariantCulture"/>. On the user's pl-PL machine
/// <c>123456789.1234m.ToString()</c> yields <c>123456789,1234</c> — a <c>Dynamic SQL Error</c>. This is
/// why <c>ExportValueFormatter</c> (CurrentCulture by design) cannot be reused here.</item>
/// <item><b>Fractional seconds.</b> <c>DateTime.ToString()</c> silently drops them; an explicit
/// <c>.ffff</c> format is mandatory.</item>
/// <item><b>The ISO <c>T</c> separator.</b> <c>ToString("o")</c> emits <c>2024-03-15T13:45:59</c>, which
/// Firebird rejects; <c>ToString(Invariant)</c> emits US-format <c>03/15/2024</c>. Only an explicit
/// format string is correct. (The same trap <c>TraceSqlInliner.NormalizeTemporal</c> documents.)</item>
/// <item><b>BLOBs.</b> <c>ExportValueFormatter</c> renders a <c>byte[]</c> as the string <c>(BLOB)</c>,
/// which in an INSERT becomes <c>'(BLOB)'</c> — silent data corruption. Binary is <c>x'…'</c> hex.</item>
/// </list>
/// </para>
/// <para>
/// <b>NULL is a value, not a predicate.</b> This writer renders <c>NULL</c>; turning that into
/// <c>IS NULL</c> where a WHERE needs it is the statement builder's job, not the literal's.
/// </para>
/// </summary>
public static class SqlLiteralWriter
{
    /// <summary>The literal for a null / <c>DBNull</c> cell, of any kind.</summary>
    public const string NullLiteral = "NULL";

    /// <summary>Renders <paramref name="value"/> as a Firebird literal of <paramref name="kind"/>, or
    /// refuses with a structured reason, under the engine's own size ceilings.</summary>
    public static SqlLiteralResult Write(object? value, SqlValueKind kind)
        => Write(value, kind, SqlLiteralLimits.Default);

    /// <summary>As <see cref="Write(object?, SqlValueKind)"/>, under caller-chosen ceilings.</summary>
    public static SqlLiteralResult Write(object? value, SqlValueKind kind, SqlLiteralLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        // NULL is faithful for every type — including a kind we could not map, whose non-null values we
        // would refuse. A row is copyable on the strength of the values it actually holds.
        if (value is null or DBNull) return SqlLiteralResult.Written(NullLiteral);

        return kind switch
        {
            SqlValueKind.Integer => WriteInteger(value),
            SqlValueKind.Decimal => WriteDecimal(value),
            SqlValueKind.Float => WriteFloat(value),
            SqlValueKind.Text => WriteText(value),
            SqlValueKind.TextBlob => WriteTextBlob(value, limits.MaxTextBlobChars),
            SqlValueKind.Date => WriteDate(value),
            SqlValueKind.Time => WriteTime(value),
            SqlValueKind.Timestamp => WriteTimestamp(value),
            SqlValueKind.Boolean => WriteBoolean(value),
            SqlValueKind.BinaryBlob => WriteBinaryBlob(value, limits.MaxBinaryBytes),
            _ => SqlLiteralResult.Refused(SqlLiteralRefusal.UnsupportedKind),
        };
    }

    // The wide integer containers (Int128/UInt128/BigInteger) are included on purpose: a NUMERIC/DECIMAL
    // with precision > 18, or a value beyond Int64, comes back from the driver as one of these — and it is
    // still an INTEGER whose exact literal is simply its digits. Refusing it (as the earlier sbyte…ulong-
    // only list did) reported "no exact SQL literal" for a value that has one, which broke Copy-as-INSERT/
    // UPDATE on large-precision keys. A value that came out of a Firebird column necessarily fits INT128,
    // so its digit literal always re-parses; InvariantCulture keeps it separator-free and sign-correct.
    private static SqlLiteralResult WriteInteger(object value) => value switch
    {
        sbyte or byte or short or ushort or int or uint or long or ulong
            or Int128 or UInt128 or System.Numerics.BigInteger
            => SqlLiteralResult.Written(Invariant(value)),
        // Some drivers/columns hand a numeric back as its STRING form (measured: an INTEGER-kind PK
        // arrived as "10019"). The declared kind is the authority that the target is an integer, so a
        // string that is an EXACT integer renders as the bare literal. STRICT on purpose: only leading
        // sign + surrounding whitespace (NumberStyles.Integer) — a culture-formatted "10 019" / "1,5" or a
        // non-numeric string fails the parse and is refused, never approximated (§0).
        string s => ParseIntegerString(s),
        _ => SqlLiteralResult.Refused(SqlLiteralRefusal.UnexpectedValueType),
    };

    // NUMERIC/DECIMAL with scale 0 comes back as an integral type — rendering its digits is exact, so
    // accept it here rather than refusing a perfectly renderable value.
    private static SqlLiteralResult WriteDecimal(object value) => value switch
    {
        decimal => SqlLiteralResult.Written(Invariant(value)),
        sbyte or byte or short or ushort or int or uint or long or ulong
            or Int128 or UInt128 or System.Numerics.BigInteger => WriteInteger(value),
        // As above, but a decimal target also accepts a fractional string. Strict invariant: sign +
        // decimal point, never a thousands separator (so pl-PL "1,5" is refused, not misread as 15).
        string s => ParseDecimalString(s),
        _ => SqlLiteralResult.Refused(SqlLiteralRefusal.UnexpectedValueType),
    };

    private static SqlLiteralResult ParseIntegerString(string value)
        => System.Numerics.BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? SqlLiteralResult.Written(i.ToString(CultureInfo.InvariantCulture))
            : SqlLiteralResult.Refused(SqlLiteralRefusal.UnexpectedValueType);

    private static SqlLiteralResult ParseDecimalString(string value)
    {
        // Integer form first (any magnitude, incl. beyond decimal's range) → bare digits.
        if (System.Numerics.BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return SqlLiteralResult.Written(i.ToString(CultureInfo.InvariantCulture));
        // Fractional form → exact decimal; NO AllowThousands, so a comma is never a grouping separator.
        const NumberStyles style = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
            | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        return decimal.TryParse(value, style, CultureInfo.InvariantCulture, out var d)
            ? SqlLiteralResult.Written(d.ToString(CultureInfo.InvariantCulture))
            : SqlLiteralResult.Refused(SqlLiteralRefusal.UnexpectedValueType);
    }

    // Default ToString is shortest-round-trippable on .NET Core 3.0+ (verified reparse-exact against the
    // engine for 0.1, e, 1E+20 and double.MaxValue), so no "R"/"G17" is needed — and G17 would emit
    // noise digits.
    private static SqlLiteralResult WriteFloat(object value)
    {
        double d;
        string render;
        switch (value)
        {
            case float f: d = f; render = Invariant(f); break;
            case double dd: d = dd; render = Invariant(dd); break;
            // A float delivered as a string (see WriteInteger's note) — strict invariant parse, exponent
            // allowed, no thousands; render the parsed value's shortest round-trippable form.
            case string s when double.TryParse(
                s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                d = parsed; render = Invariant(parsed); break;
            default: return SqlLiteralResult.Refused(SqlLiteralRefusal.UnexpectedValueType);
        }

        // NaN / ±Infinity have no Firebird literal at all — refuse rather than emit an
        // identifier-looking `NaN` into a statement.
        if (!double.IsFinite(d)) return SqlLiteralResult.Refused(SqlLiteralRefusal.NotRepresentable);

        // MEASURED, and the reason this is not an `if (finite) write it`: Firebird's literal parser
        // cannot represent a SUBNORMAL double. `select cast(5E-324 as double precision)` is accepted —
        // and returns 0. The literal parses, the statement succeeds, and the value is destroyed: silent
        // data loss, the one failure §0 forbids, and invisible to any string-equality test. The boundary
        // is exactly the normal/subnormal line (5E-324, 1E-320 and 1E-310 all → 0; the smallest normal
        // double, 2.2250738585072014E-308, round-trips exactly).
        //
        // Testing the value AS A DOUBLE is deliberate, not incidental: a subnormal FLOAT (~1.4E-45) is a
        // perfectly NORMAL double, so it goes through the parser intact and round-trips exactly through
        // a FLOAT column — verified. Refusing it would be a false refusal. This predicate is the
        // mechanism, not an approximation of it.
        if (double.IsSubnormal(d)) return SqlLiteralResult.Refused(SqlLiteralRefusal.NotRepresentable);

        // Render the shortest round-trippable form (a float keeps float's form; a parsed string keeps the
        // parsed value's).
        return SqlLiteralResult.Written(render);
    }

    // No ceiling: a CHAR/VARCHAR value is already bounded by the same limit a literal is.
    private static SqlLiteralResult WriteText(object value) => value switch
    {
        string s => SqlLiteralResult.Written(Quote(s)),
        char c => SqlLiteralResult.Written(Quote(c.ToString())),
        _ => SqlLiteralResult.Refused(SqlLiteralRefusal.UnexpectedValueType),
    };

    // A text BLOB arrives already decoded as a string, but — unlike a VARCHAR — it can be megabytes,
    // which no single literal can carry. This is the one text path that needs the ceiling.
    private static SqlLiteralResult WriteTextBlob(object value, int maxTextBlobChars) => value switch
    {
        string s when s.Length > maxTextBlobChars => SqlLiteralResult.Refused(SqlLiteralRefusal.TooLarge),
        string or char => WriteText(value),
        _ => SqlLiteralResult.Refused(SqlLiteralRefusal.UnexpectedValueType),
    };

    // The `string` branch on each temporal/boolean writer handles the same driver-delivers-a-string case
    // as the numeric writers (see WriteInteger). It parses with InvariantCulture ONLY — the driver's own
    // string form is invariant/ISO (the executor reads values through the driver, never through a UI
    // culture) — and re-dispatches to the typed path, so all the representable-ness checks (a DATE's zero
    // time-of-day, sub-0.1 ms precision, TIME range) still apply. A string it cannot parse invariantly
    // (e.g. a pl-PL "15.03.2024") is REFUSED, never guessed into another calendar date (§0).
    private static SqlLiteralResult WriteDate(object value) => value switch
    {
        // A DATE column's value always has a zero time-of-day. A non-zero one means the kind and the
        // value disagree, and `yyyy-MM-dd` would silently drop real data — refuse instead.
        DateTime { TimeOfDay.Ticks: 0 } dt
            => SqlLiteralResult.Written(Quote(dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
        DateTime => SqlLiteralResult.Refused(SqlLiteralRefusal.NotRepresentable),
        string s when TryParseIsoDateTime(s, out var dt) => WriteDate(dt),
        _ => SqlLiteralResult.Refused(SqlLiteralRefusal.UnexpectedValueType),
    };

    private static SqlLiteralResult WriteTime(object value) => value switch
    {
        TimeSpan ts when ts < TimeSpan.Zero || ts >= OneDay
            => SqlLiteralResult.Refused(SqlLiteralRefusal.NotRepresentable),
        TimeSpan ts when HasSubFirebirdPrecision(ts.Ticks)
            => SqlLiteralResult.Refused(SqlLiteralRefusal.NotRepresentable),
        TimeSpan ts
            => SqlLiteralResult.Written(Quote(ts.ToString(@"hh\:mm\:ss\.ffff", CultureInfo.InvariantCulture))),
        string s when TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts) => WriteTime(ts),
        _ => SqlLiteralResult.Refused(SqlLiteralRefusal.UnexpectedValueType),
    };

    private static SqlLiteralResult WriteTimestamp(object value) => value switch
    {
        DateTime dt when HasSubFirebirdPrecision(dt.Ticks)
            => SqlLiteralResult.Refused(SqlLiteralRefusal.NotRepresentable),
        // Space separator, never ISO 'T' (rejected by the engine); fractional seconds always.
        DateTime dt
            => SqlLiteralResult.Written(Quote(dt.ToString("yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture))),
        string s when TryParseIsoDateTime(s, out var dt) => WriteTimestamp(dt),
        _ => SqlLiteralResult.Refused(SqlLiteralRefusal.UnexpectedValueType),
    };

    private static SqlLiteralResult WriteBoolean(object value) => value switch
    {
        bool b => SqlLiteralResult.Written(b ? "true" : "false"),
        string s when bool.TryParse(s, out var b) => WriteBoolean(b),
        _ => SqlLiteralResult.Refused(SqlLiteralRefusal.UnexpectedValueType),
    };

    private static SqlLiteralResult WriteBinaryBlob(object value, int maxBinaryBytes) => value switch
    {
        byte[] b when b.Length > maxBinaryBytes => SqlLiteralResult.Refused(SqlLiteralRefusal.TooLarge),
        byte[] b => SqlLiteralResult.Written("x'" + Convert.ToHexString(b) + "'"),
        _ => SqlLiteralResult.Refused(SqlLiteralRefusal.UnexpectedValueType),
    };

    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    // The ONLY date/timestamp string forms accepted when a temporal value arrives as a string (see the
    // temporal writers' note). Deliberately ISO-ONLY and exact: ISO has exactly one interpretation, so a
    // string either is an ISO date/timestamp (parsed faithfully) or is refused — there is no lenient path
    // that could read `03/04/2024` or a pl-PL `04.03.2024` as the wrong calendar day (§0). If a real
    // Firebird environment is found to hand back some other canonical form, add that exact format here.
    private static readonly string[] IsoDateTimeFormats =
    {
        "yyyy-MM-dd HH:mm:ss.ffff", "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss.ff",
        "yyyy-MM-dd HH:mm:ss.f", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
    };

    private static bool TryParseIsoDateTime(string value, out DateTime result)
        => DateTime.TryParseExact(
            value, IsoDateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    // Firebird's TIME/TIMESTAMP resolution is 1/10000 s = 1000 ticks. A value carrying finer precision
    // cannot come from a Firebird column, and `.ffff` would silently truncate it — so it is refused
    // rather than rendered. (This writer is the one value→SQL renderer; a future non-Firebird feeder
    // must not be able to lose data through it.)
    private static bool HasSubFirebirdPrecision(long ticks) => ticks % 1000 != 0;

    private static string Invariant(object value)
        => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
