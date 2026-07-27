using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Import;

/// <summary>
/// How a delimited text source (CSV / TXT / clipboard) is split into records and fields.
/// <para>
/// Every member is a DECLARED decision, never a sniffed one. The two <c>AutoDetect…</c> flags say
/// "propose a value for me and show me why" — the detector fills <see cref="Delimiter"/> /
/// <see cref="EncodingName"/> and the UI displays the basis; the value the reader uses is always the one
/// stored here. That is what §0.4 means by "the culture is declared, not silently detected".
/// </para>
/// </summary>
public sealed record DelimitedOptions
{
    /// <summary>Field separator actually used by the reader.</summary>
    public char Delimiter { get; init; } = ';';

    /// <summary>Ask the detector to propose <see cref="Delimiter"/> when the source is (re)read. The proposal
    /// is written back into <see cref="Delimiter"/>, so the reader has exactly one input.</summary>
    public bool AutoDetectDelimiter { get; init; } = true;

    /// <summary>The quoting character (RFC 4180 <c>"</c>). A field may contain the delimiter, a line break, or
    /// the quote itself doubled, when quoted.</summary>
    public char Quote { get; init; } = '"';

    /// <summary>Firebird-style charset name of the FILE (not of the connection) — e.g. <c>WIN1250</c>,
    /// <c>UTF8</c>. Resolved to a .NET <c>Encoding</c> by <c>CharsetCatalog</c>, so the vocabulary matches the
    /// one the connection profile already uses.</summary>
    public string EncodingName { get; init; } = "WIN1250";

    /// <summary>Ask the detector to propose <see cref="EncodingName"/> (BOM first, then a heuristic).</summary>
    public bool AutoDetectEncoding { get; init; } = true;

    public LineEndingMode LineEnding { get; init; } = LineEndingMode.Auto;

    /// <summary>The first physical record carries column names.</summary>
    public bool HasHeader { get; init; } = true;

    /// <summary>1-based index of the first record to import. With <see cref="HasHeader"/> the natural value is
    /// 2; it is a separate setting because a file can carry banner lines above the header.</summary>
    public int FirstDataRow { get; init; } = 2;

    /// <summary>1-based index of the last record to import, or <c>null</c> for "to the end". Never
    /// <c>int.MaxValue</c> — that is an implementation detail leaking into the UI, which is precisely what §8
    /// point 7 criticises in the tool we are replacing.</summary>
    public int? LastRow { get; init; }

    /// <summary>Trim leading/trailing whitespace from every unquoted field. Off by default: trimming is a
    /// change to the data, so it is the user's decision, not the reader's.</summary>
    public bool TrimWhitespace { get; init; }

    /// <summary>The literal that means SQL NULL in a text source. Default <c>""</c> — an empty field is NULL.
    /// <para>
    /// Applies to TEXT sources only. A spreadsheet's blank cell carries no literal at all, so that question is
    /// answered by <see cref="ImportBehaviorOptions.TreatEmptyAsNull"/> instead — one question, one owner,
    /// even though both read as "empty means NULL" in English.
    /// </para>
    /// </summary>
    public string NullToken { get; init; } = string.Empty;
}

/// <summary>
/// How a spreadsheet source is read. Kept separate from <see cref="DelimitedOptions"/> rather than merged into
/// one bag with unused halves: the Format section renders whichever block the provider's capabilities declare,
/// so an unused option cannot be displayed or persisted by accident.
/// </summary>
public sealed record SpreadsheetOptions
{
    /// <summary>0-based sheet index — the identity the workbook itself guarantees.</summary>
    public int SheetIndex { get; init; }

    /// <summary>Sheet name as it was when the configuration was made. Advisory only: kept so a reloaded
    /// profile can say "the sheet named X is now at a different position" instead of silently reading whatever
    /// sheet happens to sit at <see cref="SheetIndex"/>.</summary>
    public string? SheetName { get; init; }

    public bool HasHeader { get; init; } = true;

    /// <summary>1-based worksheet row of the first data row.</summary>
    public int FirstDataRow { get; init; } = 2;

    /// <summary>1-based worksheet row of the last data row, or <c>null</c> for "to the end".</summary>
    public int? LastRow { get; init; }

    /// <summary>Read a numeric cell whose number format is a date format as a date rather than as the raw
    /// serial number. I0 measured that this is the ONLY signal available — the cell itself is just a number
    /// (design R3).</summary>
    public bool DatesAsDates { get; init; } = true;
}

/// <summary>
/// How text becomes a typed value: the separators, the date field order, and the token vocabularies.
/// <b>Declared, never guessed</b> (§0.4) — the converter refuses a value it cannot read under these settings
/// instead of trying another interpretation.
/// </summary>
public sealed record ImportCultureOptions
{
    /// <summary>PL default.</summary>
    public char DecimalSeparator { get; init; } = ',';

    /// <summary>Group separator to strip before parsing, or <c>null</c> when the source uses none. A space
    /// here matters: I0 found a real file whose numeric column contained one non-numeric cell, and a
    /// mis-declared group separator turns a whole column into row errors.</summary>
    public char? ThousandsSeparator { get; init; }

    public DateFieldOrder DateOrder { get; init; } = DateFieldOrder.Dmy;

    public char DateSeparator { get; init; } = '.';

    public char TimeSeparator { get; init; } = ':';

    /// <summary>Tokens read as boolean true, case-insensitively.</summary>
    public IReadOnlyList<string> TrueTokens { get; init; } = DefaultTrueTokens;

    /// <summary>Tokens read as boolean false, case-insensitively.</summary>
    public IReadOnlyList<string> FalseTokens { get; init; } = DefaultFalseTokens;

    public static readonly IReadOnlyList<string> DefaultTrueTokens =
        new[] { "1", "T", "TRUE", "Y", "YES", "TAK", "PRAWDA" };

    public static readonly IReadOnlyList<string> DefaultFalseTokens =
        new[] { "0", "F", "FALSE", "N", "NO", "NIE", "FAŁSZ" };

    /// <summary>The <see cref="System.Globalization.NumberFormatInfo"/> these settings describe. Built here so
    /// the converter has one source for "what do these separators mean", instead of every call site
    /// re-deriving it.</summary>
    public System.Globalization.NumberFormatInfo BuildNumberFormat()
    {
        var format = (System.Globalization.NumberFormatInfo)
            System.Globalization.CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberDecimalSeparator = DecimalSeparator.ToString();
        // No group separator declared ⇒ make grouping unusable rather than leaving the invariant ",",
        // which would silently accept "1,234" as 1234 under a "," decimal separator.
        format.NumberGroupSeparator = ThousandsSeparator?.ToString() ?? "  ";
        return format;
    }

    /// <summary>True when <paramref name="token"/> is one of the declared true tokens.</summary>
    public bool IsTrueToken(string token) => Contains(TrueTokens, token);

    /// <summary>True when <paramref name="token"/> is one of the declared false tokens.</summary>
    public bool IsFalseToken(string token) => Contains(FalseTokens, token);

    private static bool Contains(IReadOnlyList<string> tokens, string token)
        => tokens.Any(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));
}
