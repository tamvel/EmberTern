using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EmberTern.Core.Export;

namespace EmberTern.Export.Office;

/// <summary>
/// Excel (.xlsx) exporter — a streaming SpreadsheetML writer built on the OpenXML SDK
/// (<see cref="OpenXmlWriter"/>), so rows flow to the package without materializing a full workbook
/// in memory. Cells are <b>typed</b> (unlike the text exporters): numbers write as invariant numeric
/// cells (Excel formats them per the viewer's locale — no CSV decimal-separator trap), dates as real
/// date cells (a single date number-format style), booleans as boolean cells, BLOBs as a
/// <c>(BLOB)</c> placeholder, NULL as an empty cell, everything else as an inline string. Writes to
/// the sink's byte <see cref="IExportSink.Stream"/> (XLSX is binary — it cannot use a TextWriter and
/// is file-only, never clipboard). A result larger than Excel's per-sheet limit is split across
/// multiple sheets (no data loss).
/// </summary>
public sealed class XlsxExporter : IExporter
{
    private const int ReportEvery = 1000;

    // Excel's hard limit is 1,048,576 rows per sheet (including the header row).
    private const long ExcelMaxRowsPerSheet = 1_048_576;

    // A single custom number format for dates. Ids < 164 are built-in/reserved.
    private const uint DateNumberFormatId = 164;
    private const uint DateStyleIndex = 1; // index into CellFormats (0 = default/General, 1 = date)

    // OLE Automation date range is unreliable below the epoch; older dates fall back to an ISO string.
    private static readonly DateTime OaDateFloor = new(1900, 1, 1);

    private readonly bool _includeHeader;
    private readonly long _maxRowsPerSheet;

    public XlsxExporter(bool includeHeader) : this(includeHeader, ExcelMaxRowsPerSheet)
    {
    }

    // Test seam: a small per-sheet limit lets sheet-splitting be verified without a 1M-row fixture.
    internal XlsxExporter(bool includeHeader, long maxRowsPerSheet)
    {
        _includeHeader = includeHeader;
        _maxRowsPerSheet = maxRowsPerSheet;
    }

    public async Task<long> ExportAsync(
        IReadOnlyList<ExportColumn> columns,
        IAsyncEnumerable<object?[]> rows,
        IExportSink sink,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        using var document = SpreadsheetDocument.Create(sink.Stream, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();

        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = BuildStylesheet();
        stylesPart.Stylesheet.Save();

        var sheetRefs = new List<(string PartId, string Name)>();
        long total = 0;
        int sheetNumber = 0;
        long maxDataRowsPerSheet = _includeHeader ? _maxRowsPerSheet - 1 : _maxRowsPerSheet;

        var enumerator = rows.GetAsyncEnumerator(cancellationToken);
        try
        {
            bool hasRow = await enumerator.MoveNextAsync().ConfigureAwait(false);

            // Always emit at least one sheet (header-only / empty for a zero-row result).
            do
            {
                sheetNumber++;
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                sheetRefs.Add((workbookPart.GetIdOfPart(worksheetPart), "Sheet" + sheetNumber.ToString(CultureInfo.InvariantCulture)));

                long rowsInSheet = 0;
                using (var writer = OpenXmlWriter.Create(worksheetPart))
                {
                    writer.WriteStartElement(new Worksheet());
                    writer.WriteStartElement(new SheetData());

                    uint excelRowIndex = 1;
                    if (_includeHeader)
                    {
                        WriteHeaderRow(writer, columns, excelRowIndex++);
                    }

                    while (hasRow && rowsInSheet < maxDataRowsPerSheet)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        WriteDataRow(writer, enumerator.Current, excelRowIndex++);
                        rowsInSheet++;
                        total++;
                        if (total % ReportEvery == 0) progress?.Report(total);
                        hasRow = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }

                    writer.WriteEndElement(); // SheetData
                    writer.WriteEndElement(); // Worksheet
                }
            }
            while (hasRow);
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        var sheets = new Sheets();
        uint sheetId = 1;
        foreach (var (partId, name) in sheetRefs)
        {
            sheets.Append(new Sheet { Id = partId, SheetId = sheetId++, Name = name });
        }
        workbookPart.Workbook = new Workbook(sheets);
        workbookPart.Workbook.Save();

        progress?.Report(total);
        return total;
    }

    private static void WriteHeaderRow(OpenXmlWriter writer, IReadOnlyList<ExportColumn> columns, uint rowIndex)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });
        foreach (var column in columns)
        {
            WriteInlineString(writer, column.Name);
        }
        writer.WriteEndElement();
    }

    private static void WriteDataRow(OpenXmlWriter writer, object?[] row, uint rowIndex)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });
        foreach (var value in row)
        {
            WriteCell(writer, value);
        }
        writer.WriteEndElement();
    }

    private static void WriteCell(OpenXmlWriter writer, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                writer.WriteElement(new Cell());
                break;
            case bool b:
                writer.WriteElement(new Cell { DataType = CellValues.Boolean, CellValue = new CellValue(b ? "1" : "0") });
                break;
            case DateTime dt when dt >= OaDateFloor:
                writer.WriteElement(new Cell
                {
                    StyleIndex = DateStyleIndex,
                    CellValue = new CellValue(dt.ToOADate().ToString(CultureInfo.InvariantCulture)),
                });
                break;
            case byte[]:
                WriteInlineString(writer, ExportValueFormatter.BlobPlaceholder);
                break;
            case sbyte or byte or short or ushort or int or uint or long or ulong or decimal or float or double:
                writer.WriteElement(new Cell { CellValue = new CellValue(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture)) });
                break;
            default:
                WriteInlineString(writer, value.ToString() ?? string.Empty);
                break;
        }
    }

    private static void WriteInlineString(OpenXmlWriter writer, string text)
        => writer.WriteElement(new Cell
        {
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }),
        });

    private static Stylesheet BuildStylesheet() => new(
        new NumberingFormats(
            new NumberingFormat { NumberFormatId = DateNumberFormatId, FormatCode = "yyyy\\-mm\\-dd\\ hh:mm:ss" })
        { Count = 1 },
        new Fonts(new Font()) { Count = 1 },
        new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
        { Count = 2 },
        new Borders(new Border()) { Count = 1 },
        new CellStyleFormats(new CellFormat()) { Count = 1 },
        new CellFormats(
            new CellFormat(),
            new CellFormat { NumberFormatId = DateNumberFormatId, ApplyNumberFormat = true })
        { Count = 2 });
}
