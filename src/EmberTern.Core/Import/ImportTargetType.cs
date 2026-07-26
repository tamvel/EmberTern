using System;
using System.Globalization;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Import;

/// <summary>
/// A target column's declared type, reduced to what the import has to decide: <b>which kind of value</b> it
/// takes, and <b>what it will not hold</b> (length, precision, scale, integer range).
/// <para>
/// ⭐ <b>This is the ONE owner of "what type is this column"</b> for the whole import module. The converter,
/// the validator, the mapping planner and the readiness evaluation all ask it — because four independent
/// re-derivations of "is VARCHAR(20) twenty characters long" is exactly how a length check and a length
/// warning end up disagreeing in front of the user.
/// </para>
/// <para>
/// <b>Why it reuses <see cref="SqlValueKind"/> rather than defining a second vocabulary</b> (design §4.6):
/// that enum already answers "which distinctions change how a value is treated", and it already records the
/// decisions that matter here — DATE and TIMESTAMP are both <c>DateTime</c> and must not be conflated, and the
/// types with no faithful representation (ARRAY, INT128, DECFLOAT, <c>WITH TIME ZONE</c>) are deliberately
/// <see cref="SqlValueKind.Unknown"/>. Export maps <c>FbDbType</c> onto it; import maps the <em>formatted type
/// text</em> onto it — the same vocabulary read in the other direction, which is precisely what §4.6
/// prescribes. Keeping the two sets of Unknowns identical is the point: a type export refuses to write is a
/// type import must refuse to fill.
/// </para>
/// <para>
/// Input is <see cref="ColumnSpec.Type"/> — the text the catalog reader already formatted
/// (<c>VARCHAR(20)</c>, <c>NUMERIC(15,2)</c>, <c>BLOB SUB_TYPE TEXT</c>, <c>DOUBLE PRECISION</c>, …). Anything
/// this parser does not recognise becomes <see cref="SqlValueKind.Unknown"/> and is refused with a reason,
/// never guessed at (§0).
/// </para>
/// </summary>
public sealed record ImportTargetType
{
    /// <summary>What kind of value the column takes; <see cref="SqlValueKind.Unknown"/> ⇒ this build cannot
    /// write it faithfully and says so.</summary>
    public SqlValueKind Kind { get; init; } = SqlValueKind.Unknown;

    /// <summary>Base type name, upper-cased and without arguments (<c>VARCHAR</c>, <c>NUMERIC</c>,
    /// <c>DOUBLE PRECISION</c>, <c>BLOB</c>).</summary>
    public string BaseTypeName { get; init; } = string.Empty;

    /// <summary>Declared length for CHAR/VARCHAR, precision for NUMERIC/DECIMAL; <c>null</c> when the type
    /// carries none.</summary>
    public int? Size { get; init; }

    /// <summary>Declared scale for NUMERIC/DECIMAL; <c>null</c> when the type carries none.</summary>
    public int? Scale { get; init; }

    /// <summary>BLOB sub type (0 binary / 1 text); <c>null</c> for every other type.</summary>
    public int? BlobSubType { get; init; }

    /// <summary>False when the column's type has no faithful import path — the import refuses it with a
    /// reason rather than writing an approximation (§0).</summary>
    public bool IsSupported => Kind != SqlValueKind.Unknown;

    /// <summary>True when the column holds text (CHAR/VARCHAR or a text BLOB).</summary>
    public bool IsText => Kind is SqlValueKind.Text or SqlValueKind.TextBlob;

    /// <summary>Declared character limit, or <c>null</c> when the column has none (a text BLOB is unbounded,
    /// so a length check there would invent a limit the database does not have).</summary>
    public int? MaxTextLength => Kind == SqlValueKind.Text ? Size : null;

    /// <summary>Total significant digits for an exact numeric, else <c>null</c>.</summary>
    public int? NumericPrecision => Kind == SqlValueKind.Decimal ? Size : null;

    /// <summary>Digits kept after the decimal point for an exact numeric. <c>NUMERIC(15)</c> keeps none.</summary>
    public int NumericScale => Kind == SqlValueKind.Decimal ? Scale ?? 0 : 0;

    /// <summary>The inclusive range an integer column accepts, or <c>null</c> when the column is not an
    /// integer. Needed because the converter must produce the column's own CLR width — a <c>short</c> for
    /// SMALLINT — and therefore has to know the value fits <em>before</em> narrowing it.</summary>
    public (long Min, long Max)? IntegerRange => Kind != SqlValueKind.Integer
        ? null
        : BaseTypeName switch
        {
            "SMALLINT" => (short.MinValue, short.MaxValue),
            "BIGINT" => (long.MinValue, long.MaxValue),
            _ => (int.MinValue, int.MaxValue),
        };

    /// <summary>Resolves the declared type of <paramref name="column"/>.</summary>
    public static ImportTargetType Resolve(ColumnSpec column) => Resolve(column?.Type);

    /// <summary>Resolves a formatted Firebird type as produced by the catalog reader.</summary>
    public static ImportTargetType Resolve(string? formattedType)
    {
        var text = (formattedType ?? string.Empty).Trim();
        if (text.Length == 0) return new ImportTargetType();

        // BLOB carries its argument as "SUB_TYPE x", not in parentheses, so it cannot go through the
        // generic "name(args)" split below.
        if (text.StartsWith("BLOB", StringComparison.OrdinalIgnoreCase)) return ResolveBlob(text);

        var baseName = text;
        int? size = null;
        int? scale = null;

        var open = text.IndexOf('(');
        if (open >= 0)
        {
            baseName = text.Substring(0, open).TrimEnd();
            var close = text.IndexOf(')', open + 1);
            var args = close > open ? text.Substring(open + 1, close - open - 1) : string.Empty;
            var parts = args.Split(',');
            if (parts.Length > 0 && TryParseArg(parts[0], out var parsedSize)) size = parsedSize;
            if (parts.Length > 1 && TryParseArg(parts[1], out var parsedScale)) scale = parsedScale;
        }

        var upper = Normalize(baseName);
        return new ImportTargetType
        {
            Kind = KindOf(upper),
            BaseTypeName = upper,
            Size = size,
            Scale = scale,
        };
    }

    private static ImportTargetType ResolveBlob(string text)
    {
        // "BLOB SUB_TYPE TEXT" / "BLOB SUB_TYPE BINARY" / "BLOB SUB_TYPE 2" — the exact forms the catalog
        // reader emits. A sub type other than 0/1 has no defined text representation, so it is Unknown and
        // gets refused rather than filled with a guess.
        var upper = Normalize(text);
        int? subType = upper switch
        {
            var t when t.EndsWith(" TEXT", StringComparison.Ordinal) => 1,
            var t when t.EndsWith(" BINARY", StringComparison.Ordinal) => 0,
            _ => TryParseTrailingInt(upper),
        };

        return new ImportTargetType
        {
            Kind = subType switch
            {
                0 => SqlValueKind.BinaryBlob,
                1 => SqlValueKind.TextBlob,
                _ => SqlValueKind.Unknown,
            },
            BaseTypeName = "BLOB",
            BlobSubType = subType,
        };
    }

    private static int? TryParseTrailingInt(string upper)
    {
        var space = upper.LastIndexOf(' ');
        if (space < 0) return null;
        return TryParseArg(upper.Substring(space + 1), out var value) ? value : null;
    }

    private static bool TryParseArg(string text, out int value)
        => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>Upper-cases and collapses internal whitespace, so <c>double  precision</c> and
    /// <c>DOUBLE PRECISION</c> resolve identically.</summary>
    private static string Normalize(string text)
    {
        var upper = text.Trim().ToUpperInvariant();
        if (upper.IndexOf("  ", StringComparison.Ordinal) < 0) return upper;

        var parts = upper.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

    /// <summary>
    /// Base type name → value kind. Deliberately an EXACT match list, which is what keeps
    /// <c>TIMESTAMP WITH TIME ZONE</c> from being read as <c>TIMESTAMP</c>: a zoned value written through the
    /// plain-timestamp path would silently lose its offset, and the export side already refuses the same
    /// types for the same reason.
    /// </summary>
    private static SqlValueKind KindOf(string baseTypeName) => baseTypeName switch
    {
        "SMALLINT" or "INTEGER" or "INT" or "BIGINT" => SqlValueKind.Integer,
        "NUMERIC" or "DECIMAL" => SqlValueKind.Decimal,
        "FLOAT" or "DOUBLE PRECISION" => SqlValueKind.Float,
        "CHAR" or "CHARACTER" or "VARCHAR" or "CHARACTER VARYING" or "CSTRING" => SqlValueKind.Text,
        "DATE" => SqlValueKind.Date,
        "TIME" => SqlValueKind.Time,
        "TIMESTAMP" => SqlValueKind.Timestamp,
        "BOOLEAN" => SqlValueKind.Boolean,

        // Everything else — ARRAY, INT128, DECFLOAT, every WITH TIME ZONE form, and any type a future
        // Firebird adds — is Unknown on purpose. Refusing loudly is safe; guessing is not (§0).
        _ => SqlValueKind.Unknown,
    };
}
