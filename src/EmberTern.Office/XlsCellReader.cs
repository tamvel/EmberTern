using System;
using EmberTern.Core.Import;
using ExcelDataReader;

namespace EmberTern.Office;

/// <summary>
/// Turns a BIFF row into the raw values a <see cref="RawRecord"/> carries.
/// <para>
/// ⭐ <b>Its job is normalisation, not decoding.</b> Unlike the <c>.xlsx</c> path — where the file hands over a
/// bare serial and <c>WorkbookCellReader</c> has to decide what it means — ExcelDataReader has already applied the
/// cell's number format and produced a <see cref="DateTime"/>. That is convenient and it is a hazard: it means a
/// second component is deciding "is this a date", and two components deciding one question is how the two
/// spreadsheet providers would come to disagree about the same workbook content. So the library's ruling is
/// re-asked of <see cref="SpreadsheetNumberFormats"/>, which stays the module's ONE owner of that decision
/// (gotcha #268 — a format code is parsed, never searched), and the calendar is
/// <see cref="ExcelSerialDate"/>'s in both directions.
/// </para>
/// </summary>
internal sealed class XlsCellReader
{
    private readonly bool _datesAsDates;
    private readonly bool _treatEmptyAsNull;
    private readonly bool _errorCellsAsNull;

    public XlsCellReader(ImportConfiguration configuration)
    {
        var spreadsheet = configuration.Spreadsheet ?? new SpreadsheetOptions();
        _datesAsDates = spreadsheet.DatesAsDates;
        _treatEmptyAsNull = configuration.Behavior.TreatEmptyAsNull;
        _errorCellsAsNull = configuration.Behavior.ExcelErrorCellsAsNull;
    }

    /// <summary>
    /// One row's values, by column.
    /// <para>
    /// The dangerous placement problem <c>.xlsx</c> has — a missing middle cell being ABSENT rather than blank, so
    /// that a positional reader shifts the rest of the row (§0.1) — does not arise here: BIFF's reader presents a
    /// fixed <c>FieldCount</c> per sheet and a missing cell as <c>DBNull</c> in its own slot. Reading by index IS
    /// reading by position in this format, which is why it is safe here and would not be there.
    /// </para>
    /// </summary>
    public object?[] ReadRow(IExcelDataReader reader)
    {
        var values = new object?[reader.FieldCount];
        for (var i = 0; i < values.Length; i++) values[i] = ReadCell(reader, i);
        return values;
    }

    /// <summary>
    /// One cell's raw value.
    /// <para>
    /// ⭐ <b>R20: an error cell is a ROW ERROR, not text.</b> A cell holding <c>#N/A</c> or <c>#REF!</c> is not
    /// the string "#N/A" — importing it as such would write something that was never data. It becomes
    /// <see cref="SourceErrorValue"/>, which the converter refuses for every target type including VARCHAR. The
    /// user can opt into <c>ExcelErrorCellsAsNull</c> to take such cells as NULL instead; that is a decision, and
    /// it is made here because it is about reading the cell.
    /// </para>
    /// </summary>
    private object? ReadCell(IExcelDataReader reader, int index)
    {
        if (reader.GetCellError(index) is { } error)
        {
            return _errorCellsAsNull ? null : new SourceErrorValue(Describe(error));
        }

        if (reader.IsDBNull(index)) return Blank();

        var value = reader.GetValue(index);
        return value switch
        {
            null => Blank(),
            DateTime date => Normalize(reader, index, date),
            string text => text.Length == 0 ? Blank() : text,
            _ => value,
        };
    }

    /// <summary>
    /// ⭐ The one place the library's date ruling is re-checked against ours.
    /// <para>
    /// Two outcomes, and both matter. When the user asked for numbers rather than dates
    /// (<c>DatesAsDates</c> off, §3.3), the value has to come back out as the serial the sheet actually stores —
    /// which is only possible because <see cref="ExcelSerialDate"/> owns the inverse. And when the format is not
    /// a date format by <see cref="SpreadsheetNumberFormats"/>' reckoning, the value stays a NUMBER even though
    /// the library produced a date, because a column of money must not become a column of dates on a difference
    /// of opinion (§0.1).
    /// </para>
    /// <para>
    /// A pure time of day arrives here as OLE's 1899-12-31 plus the time; it leaves as a <see cref="TimeSpan"/>,
    /// which is what the <c>.xlsx</c> provider yields for the same cell and what the converter maps onto a TIME
    /// column.
    /// </para>
    /// </summary>
    private object Normalize(IExcelDataReader reader, int index, DateTime value)
    {
        var serial = ExcelSerialDate.ToSerial(value);

        if (!_datesAsDates) return serial;

        var numberFormatId = reader.GetNumberFormatIndex(index);
        var formatCode = reader.GetNumberFormatString(index);
        if (numberFormatId < 0
            || !SpreadsheetNumberFormats.IsDateFormat((uint)numberFormatId, formatCode))
        {
            return serial;
        }

        // The library's own DateTime is kept rather than rebuilt from the serial: it is the same instant, and a
        // needless round trip through a double could only lose a tick. The serial is used for the one thing the
        // DateTime cannot express — a value below 1, which is a time of day and no date at all.
        return serial < 1d ? TimeSpan.FromDays(serial) : value;
    }

    /// <summary>A blank cell. Whether "blank means NULL" is the user's declared behaviour, not the reader's
    /// assumption — the same <c>ImportBehaviorOptions.TreatEmptyAsNull</c> the <c>.xlsx</c> reader obeys.</summary>
    private object? Blank() => _treatEmptyAsNull ? null : string.Empty;

    /// <summary>The error cell's Excel spelling, so the report quotes what the user sees in the sheet.</summary>
    private static string Describe(CellError error) => error switch
    {
        CellError.NULL => "#NULL!",
        CellError.DIV0 => "#DIV/0!",
        CellError.VALUE => "#VALUE!",
        CellError.REF => "#REF!",
        CellError.NAME => "#NAME?",
        CellError.NUM => "#NUM!",
        CellError.NA => "#N/A",
        _ => "#ERROR",
    };
}
