using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EmberTern.Core.Import;

namespace EmberTern.Office;

/// <summary>
/// Turns worksheet cells into the raw values a <see cref="RawRecord"/> carries. Built once per read and reused
/// for every row, because the two things it needs — the shared-string table and the style→number-format map —
/// are workbook-wide and reading them per row would be quadratic.
/// </summary>
internal sealed class WorkbookCellReader
{
    /// <summary>Excel serial 61 is 1900-03-01, the first day on which Excel's calendar and
    /// <see cref="DateTime.FromOADate"/> agree. See <see cref="FromSerial"/>.</summary>
    private const double FirstUnambiguousSerial = 61d;

    private readonly IReadOnlyList<string> _sharedStrings;
    private readonly bool[] _styleIsDate;
    private readonly bool _datesAsDates;
    private readonly bool _treatEmptyAsNull;
    private readonly bool _errorCellsAsNull;

    public WorkbookCellReader(WorkbookPart workbookPart, ImportConfiguration configuration)
    {
        var spreadsheet = configuration.Spreadsheet ?? new SpreadsheetOptions();
        _datesAsDates = spreadsheet.DatesAsDates;
        _treatEmptyAsNull = configuration.Behavior.TreatEmptyAsNull;
        _errorCellsAsNull = configuration.Behavior.ExcelErrorCellsAsNull;

        _sharedStrings = ReadSharedStrings(workbookPart);
        _styleIsDate = BuildStyleDateMap(workbookPart);
    }

    /// <summary>
    /// ⭐ <b>REK-6 (2): values are placed by <c>CellReference</c>, never by order of appearance.</b>
    /// <para>
    /// I0 measured that a row with an empty middle column yields <c>[A3, C3]</c> — cell B is <b>absent</b>, not
    /// blank. A reader appending values positionally would put C's content into column B and shift the entire
    /// rest of the row by one, with no error anywhere. That is §0.1 in its purest form, and it is the single
    /// most dangerous thing about reading a spreadsheet.
    /// </para>
    /// <para>
    /// The running index is a fallback for the one legal case where a cell carries no reference at all (some
    /// generators omit it), where "the next column" IS the format's own meaning. A cell that HAS a reference is
    /// always placed by it.
    /// </para>
    /// </summary>
    public object?[] ReadRow(Row row)
    {
        var values = new List<object?>();
        var nextColumn = 0;

        foreach (var cell in row.Elements<Cell>())
        {
            var column = ColumnIndex(cell.CellReference?.Value);
            if (column < 0) column = nextColumn;
            nextColumn = column + 1;

            while (values.Count <= column) values.Add(null);
            values[column] = ReadCell(cell);
        }

        return values.ToArray();
    }

    /// <summary>
    /// One cell's raw value.
    /// <para>
    /// ⭐ <b>REK-6 (7): a formula contributes its CACHED value, and an error cell is a ROW ERROR.</b> A cell
    /// holding <c>#N/A</c> or <c>#REF!</c> is not the text "#N/A" — importing it as such would write something
    /// that was never data (design R20). It becomes <see cref="SourceErrorValue"/>, which the converter refuses
    /// for every target type including VARCHAR. The user can opt into <c>ExcelErrorCellsAsNull</c> to take such
    /// cells as NULL instead; that is a decision, and it is made here because it is about reading the cell.
    /// </para>
    /// </summary>
    private object? ReadCell(Cell cell)
    {
        var type = cell.DataType?.Value;
        var text = cell.CellValue?.Text;

        if (type == CellValues.SharedString)
        {
            // REK-6 (5): the value is an INDEX into the shared-string table, not the text itself.
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                && i >= 0 && i < _sharedStrings.Count
                    ? AsText(_sharedStrings[i])
                    : Blank();
        }

        if (type == CellValues.InlineString)
        {
            // Measured in I0: an inline string leaves CellValue NULL — the text lives in InlineString/Text.
            return AsText(cell.InlineString?.Text?.Text ?? cell.InnerText);
        }

        if (type == CellValues.Error)
        {
            return _errorCellsAsNull ? null : new SourceErrorValue(string.IsNullOrEmpty(text) ? "#ERROR" : text);
        }

        if (type == CellValues.Boolean)
        {
            return string.IsNullOrEmpty(text) ? Blank() : !string.Equals(text, "0", StringComparison.Ordinal);
        }

        if (type == CellValues.Date)
        {
            // A rare ISO-8601 date cell. Parsed invariantly — the workbook's own format, not the user's culture.
            return DateTime.TryParse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate)
                    ? isoDate
                    : AsText(text);
        }

        if (type == CellValues.String)
        {
            // A formula whose cached result is text.
            return AsText(text);
        }

        // No DataType ⇒ a number, or a formula whose cached result is a number.
        if (string.IsNullOrEmpty(text)) return Blank();

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            // Not parseable as a number after all. Hand the text on unchanged rather than invent a value — the
            // converter is the one place allowed to say "I cannot read this" (§0.1).
            return AsText(text);
        }

        // ⭐ REK-6 (4): a date is a number PLUS a date number-format. There is no other signal — I0 confirmed the
        // cell itself is just 45000. Where the format does not clearly say "date", the value stays a NUMBER and
        // the preview shows it as one, rather than a date being guessed into existence.
        if (_datesAsDates && IsDateStyle(cell.StyleIndex?.Value))
        {
            return FromSerial(number);
        }

        return number;
    }

    /// <summary>
    /// Excel's serial number → a CLR value.
    /// <para>
    /// ⚠ Excel and <see cref="DateTime.FromOADate"/> do not share an epoch below 1900-03-01: Excel's serial 1 is
    /// 1900-01-01 while OLE's is 1899-12-31, and Excel additionally carries the phantom 1900-02-29 (serial 60)
    /// that never existed. Blindly calling <c>FromOADate</c> would shift every January/February 1900 date by a
    /// day — silently. So the pre-1900-03-01 range is corrected explicitly, and the phantom day is refused: it
    /// stays a number, which the converter will reject for a DATE column with an honest message rather than
    /// inventing a date that has no calendar (§0.1).
    /// </para>
    /// </summary>
    private static object FromSerial(double serial)
    {
        // A pure time of day (Excel formats 45–47): no date part at all, so a TimeSpan says exactly that. The
        // converter maps it straight onto a TIME column.
        if (serial >= 0d && serial < 1d) return TimeSpan.FromDays(serial);

        if (serial >= FirstUnambiguousSerial)
        {
            return IsRepresentable(serial) ? DateTime.FromOADate(serial) : serial;
        }

        // 1 ≤ serial < 60 — before the phantom day, so Excel is exactly one ahead of OLE.
        if (serial >= 1d && serial < 60d) return DateTime.FromOADate(serial + 1d);

        // serial < 0, or the phantom 1900-02-29. Not a date we can name truthfully.
        return serial;
    }

    private static bool IsRepresentable(double serial)
        => serial <= 2958465.9999999d; // 9999-12-31, FromOADate's upper bound

    private bool IsDateStyle(uint? styleIndex)
    {
        var index = styleIndex ?? 0u;
        return index < _styleIsDate.Length && _styleIsDate[index];
    }

    /// <summary>A blank cell. Whether "blank means NULL" is the user's declared behaviour, not the reader's
    /// assumption — and it is a SPREADSHEET question, which is why it lives on
    /// <c>ImportBehaviorOptions.TreatEmptyAsNull</c> and not on the text provider's NULL token.</summary>
    private object? Blank() => _treatEmptyAsNull ? null : string.Empty;

    private object? AsText(string? value)
        => string.IsNullOrEmpty(value) ? Blank() : value;

    private static int ColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference)) return -1;

        var index = 0;
        var letters = 0;
        foreach (var c in cellReference)
        {
            var upper = (char)(c & ~0x20); // ASCII upper-case; digits fall out of the range test below
            if (upper is < 'A' or > 'Z') break;
            index = index * 26 + (upper - 'A' + 1);
            letters++;
        }

        return letters == 0 ? -1 : index - 1; // A → 0
    }

    /// <summary>
    /// ⭐ <b>REK-6 (5): the shared-string table is read ONCE.</b> I0 measured Excel storing every text cell as an
    /// index into it (8 261 distinct items for 8 724 rows), so a provider that skipped it would read numbers
    /// where the user sees words. Read through <see cref="OpenXmlReader"/> for the same reason rows are.
    /// </summary>
    private static IReadOnlyList<string> ReadSharedStrings(WorkbookPart workbookPart)
    {
        var part = workbookPart.SharedStringTablePart;
        if (part is null) return Array.Empty<string>();

        var items = new List<string>();
        using var reader = OpenXmlReader.Create(part);
        while (reader.Read())
        {
            if (reader.ElementType != typeof(SharedStringItem)) continue;
            if (reader.LoadCurrentElement() is SharedStringItem item) items.Add(item.InnerText);
        }
        return items;
    }

    /// <summary>
    /// Flattens "style index → is this a date format" once per workbook, so reading a cell is an array lookup.
    /// The decision itself belongs to <see cref="SpreadsheetNumberFormats"/> — see there for why it parses the
    /// format code instead of searching it.
    /// </summary>
    private static bool[] BuildStyleDateMap(WorkbookPart workbookPart)
    {
        var stylesheet = workbookPart.WorkbookStylesPart?.Stylesheet;
        var cellFormats = stylesheet?.CellFormats?.Elements<CellFormat>().ToList();
        if (cellFormats is null || cellFormats.Count == 0) return Array.Empty<bool>();

        var customCodes = stylesheet?.NumberingFormats?.Elements<NumberingFormat>()
            .Where(n => n.NumberFormatId?.Value is not null)
            .GroupBy(n => n.NumberFormatId!.Value)
            .ToDictionary(g => g.Key, g => g.First().FormatCode?.Value);

        var map = new bool[cellFormats.Count];
        for (var i = 0; i < cellFormats.Count; i++)
        {
            var numberFormatId = cellFormats[i].NumberFormatId?.Value ?? 0u;
            string? code = null;
            customCodes?.TryGetValue(numberFormatId, out code);
            map[i] = SpreadsheetNumberFormats.IsDateFormat(numberFormatId, code);
        }
        return map;
    }
}
