using System;
using System.Collections.Generic;
using System.Text;
using EmberTern.Core.Connections;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Import;

/// <summary>
/// Builds the <see cref="Encoding"/> used to answer "can the connection carry this character" — and the one
/// setting that makes the answer meaningful.
/// <para>
/// ⭐ <b>`EncoderExceptionFallback` is the whole point.</b> I0 measured that a character absent from the
/// CONNECTION charset is written as <c>?</c> with <b>no error at all</b> — even when the target column is UTF8,
/// because the connection charset decides, not the column's (design R1). A .NET encoder does the same thing by
/// default: it substitutes silently. So an encoding built the ordinary way would cheerfully confirm that
/// <c>Ж</c> "fits" WIN1250, and the module would reproduce the exact corruption it exists to prevent. The
/// exception fallback is the single line that turns "we will damage it" into "we will detect it".
/// </para>
/// <para>
/// ⭐ <b>The scope note this used to carry is RESOLVED (Phase 5, 2026-08-15).</b> It read: <i>"the same silent
/// substitution reaches other parts of EmberTern … if the audit promotes a shared guard, this becomes a call to
/// it."</i> The audit did, and this is now exactly that — a thin forwarder onto
/// <see cref="CharsetRepresentation"/>, the one owner of "can this connection carry this text". ⛔ Do not
/// re-implement the check here; two copies is how the two answers drift.
/// </para>
/// <para>
/// ⚠⚠ <b>Forwarding fixed a live defect, so this is not a cosmetic re-point.</b> The old body resolved through
/// <c>CharsetCatalog.Resolve</c>, whose <c>_ =&gt; Encoding.UTF8</c> branch swallows <c>NONE</c> — a charset the
/// connection dialog offers. UTF-8 short-circuits the check, so <b>an import over a <c>NONE</c> connection was
/// told every value fits while the driver silently rewrote them</b> (the driver's <c>NONE</c> is the process
/// culture's ANSI code page — single-byte and lossy). The shared oracle asks
/// <see cref="CharsetCatalog.ResolveWireEncoding"/> instead, which answers that question correctly.
/// </para>
/// <para>
/// ⚠ The module's CONTRACT is unchanged: an unrepresentable value is still one failed row carrying
/// <see cref="ImportDiagnosticCode.NotRepresentableInConnectionCharset"/> and its source row number, never an
/// aborted run.
/// </para>
/// </summary>
public static class ImportCharsetGuard
{
    /// <summary>
    /// The connection's charset as an encoding that THROWS on an unrepresentable character instead of
    /// substituting one. Forwards to <see cref="CharsetRepresentation.Strict"/>.
    /// </summary>
    public static Encoding Strict(string? firebirdCharset)
        => CharsetRepresentation.Strict(firebirdCharset);

    /// <summary>True when every character of <paramref name="text"/> survives <paramref name="encoding"/>
    /// unchanged. Forwards to <see cref="CharsetRepresentation.CanRepresent"/>.</summary>
    public static bool CanRepresent(string? text, Encoding? encoding)
        => CharsetRepresentation.CanRepresent(text, encoding);

    /// <summary>Counts the values in <paramref name="samples"/> that the connection charset would damage.
    /// Feeds the readiness strip's warning (<see cref="ImportDiagnosticCode.NotRepresentableInConnectionCharset"/>)
    /// so the user is told BEFORE the run, not after the data is already <c>?</c>.</summary>
    public static int CountUnrepresentable(IEnumerable<string?>? samples, Encoding? encoding)
    {
        if (samples is null || encoding is null) return 0;
        if (encoding.CodePage == Encoding.UTF8.CodePage) return 0;

        var count = 0;
        foreach (var sample in samples)
        {
            if (!CanRepresent(sample, encoding)) count++;
        }
        return count;
    }
}

/// <summary>
/// ⭐ Pipeline step 4 (§4.4): decides whether a column may HOLD the value the converter produced.
/// <para>
/// Four questions, and they are deliberately the four the converter does not answer — the converter says what
/// a value <em>is</em>, this says what the column <em>accepts</em>:
/// <list type="number">
/// <item><b>NOT NULL</b> — a mapped column appears in the INSERT's field list, so a null value is written as
/// NULL and the column's DEFAULT does not apply. That is why nullability is checked here even for a column
/// that has a default; the "unmapped column with a default is fine" case is a different question, answered by
/// <see cref="ImportMappingPlanner"/> before any row is read.</item>
/// <item><b>Length</b> — over-long text is an error by default, and is shortened only when the user explicitly
/// turned trimming on, in which case the row is still reported with its ORIGINAL value (§0.2).</item>
/// <item><b>Precision and scale</b> — a value carrying more decimals than the column keeps would be silently
/// rounded on the way in. Refused (§0.1). There is no "round it anyway" option, because adding one is a design
/// decision rather than an implementation detail.</item>
/// <item><b>Connection charset</b> — see <see cref="ImportCharsetGuard"/>. Mandatory, not an optimisation.</item>
/// </list>
/// </para>
/// </summary>
public static class ImportRowValidator
{
    /// <summary>Validates a converted value for <paramref name="column"/>. Convenience overload — the pipeline
    /// resolves the type once per column and uses the fast overload below.</summary>
    public static ImportValueResult Validate(
        object? value,
        ColumnSpec column,
        ImportBehaviorOptions behavior,
        Encoding? connectionEncoding,
        string? rawText = null)
    {
        if (column is null) throw new ArgumentNullException(nameof(column));
        return Validate(value, ImportTargetType.Resolve(column), column.NotNull, behavior, connectionEncoding, rawText);
    }

    /// <summary>Validates a converted value against an already-resolved target type.</summary>
    /// <param name="value">The converter's output.</param>
    /// <param name="type">The column's resolved type.</param>
    /// <param name="notNull">Whether the column is declared NOT NULL.</param>
    /// <param name="behavior">The user's explicit yes/no decisions (only <c>TrimTooLongValues</c> is read).</param>
    /// <param name="connectionEncoding">The CONNECTION charset, built by <see cref="ImportCharsetGuard.Strict"/>;
    /// <c>null</c> skips the check (used by tests and by a dry run with no connection).</param>
    /// <param name="rawText">The source text, kept for the report.</param>
    public static ImportValueResult Validate(
        object? value,
        ImportTargetType type,
        bool notNull,
        ImportBehaviorOptions behavior,
        Encoding? connectionEncoding,
        string? rawText = null)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (behavior is null) throw new ArgumentNullException(nameof(behavior));

        if (value is null || value is DBNull)
        {
            return notNull
                ? ImportValueResult.Fail(ImportErrorKind.NullNotAllowed, rawText)
                : ImportValueResult.Null;
        }

        if (value is string text) return ValidateText(text, type, behavior, connectionEncoding, rawText);

        if (value is decimal number && type.Kind == SqlValueKind.Decimal)
            return ValidateDecimal(number, type, rawText);

        return ImportValueResult.Ok(value);
    }

    private static ImportValueResult ValidateText(
        string text,
        ImportTargetType type,
        ImportBehaviorOptions behavior,
        Encoding? connectionEncoding,
        string? rawText)
    {
        var value = text;
        var trimmed = false;

        var limit = type.MaxTextLength;
        if (limit is > 0 && value.Length > limit.Value)
        {
            // Firebird REJECTS an over-long string (measured — it never truncates on its own), so this is not
            // a guard against the engine: it is the user's own opt-in, and it loses data, so it is loud.
            if (!behavior.TrimTooLongValues)
                return ImportValueResult.Fail(ImportErrorKind.ValueTooLong, rawText ?? text);

            value = value.Substring(0, limit.Value);
            trimmed = true;
        }

        if (!ImportCharsetGuard.CanRepresent(value, connectionEncoding))
        {
            return ImportValueResult.Fail(
                ImportErrorKind.NotRepresentableInConnectionCharset, rawText ?? text);
        }

        return trimmed ? ImportValueResult.Trimmed(value, rawText ?? text) : ImportValueResult.Ok(value);
    }

    private static ImportValueResult ValidateDecimal(decimal value, ImportTargetType type, string? rawText)
    {
        var scale = type.NumericScale;

        // Compare by VALUE, not by the decimal's stored scale: 1.50 and 1.5 are the same number, and flagging
        // the trailing zero would refuse a perfectly exact value. Rounding that changes nothing is not a loss;
        // rounding that changes the number is (§0.1).
        if (scale is >= 0 and <= 28)
        {
            var rounded = Math.Round(value, scale, MidpointRounding.ToEven);
            if (rounded != value) return ImportValueResult.Fail(ImportErrorKind.PrecisionWouldBeLost, rawText);
        }

        var precision = type.NumericPrecision;
        if (precision is > 0)
        {
            var integerDigits = precision.Value - scale;
            // Above 28 integer digits the bound is not representable as a decimal, so the check is skipped and
            // the engine decides — silence here is honest, a wrong bound would not be.
            if (integerDigits is >= 0 and <= 28)
            {
                var bound = Pow10(integerDigits);
                if (Math.Abs(value) >= bound)
                    return ImportValueResult.Fail(ImportErrorKind.ValueOutOfRange, rawText);
            }
        }

        return ImportValueResult.Ok(value);
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++) result *= 10m;
        return result;
    }
}
