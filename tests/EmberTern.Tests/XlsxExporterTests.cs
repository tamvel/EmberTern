using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EmberTern.App.Export;
using EmberTern.Core.Export;
using EmberTern.Core.Query;
using EmberTern.Office;
using Xunit;

namespace EmberTern.Tests;

// Etap 4 — the streaming XLSX exporter. Verified by writing a real .xlsx and reading it back with the
// OpenXML SDK (typed cells, date style, empty/blob handling, header toggle, multi-sheet splitting).
public class XlsxExporterTests
{
    private static readonly ExportColumn[] Columns =
    {
        new("Name", typeof(string)),
        new("Qty", typeof(int)),
        new("Price", typeof(decimal)),
        new("When", typeof(DateTime)),
        new("Flag", typeof(bool)),
    };

    [Fact]
    public async Task Xlsx_WritesHeader_AndTypedCells()
    {
        var when = new DateTime(2026, 1, 2, 3, 4, 5);
        var rows = new object?[][] { new object?[] { "item", 42, 1.5m, when, true } };

        await WithExportedFile(new XlsxExporter(includeHeader: true), Columns, rows, path =>
        {
            var sheetRows = ReadFirstSheetRows(path);
            Assert.Equal(2, sheetRows.Count); // header + 1 data row

            Assert.Equal(new[] { "Name", "Qty", "Price", "When", "Flag" }, sheetRows[0].Select(InlineText));

            var data = sheetRows[1];
            Assert.Equal("item", InlineText(data[0]));
            Assert.Null(data[1].DataType);                       // number (default type)
            Assert.Equal("42", data[1].CellValue!.Text);
            Assert.Equal("1.5", data[2].CellValue!.Text);        // invariant decimal — no locale separator
            Assert.Equal(1u, data[3].StyleIndex!.Value);         // date style
            Assert.Equal(when.ToOADate(), double.Parse(data[3].CellValue!.Text!, CultureInfo.InvariantCulture), 6);
            Assert.Equal(CellValues.Boolean, data[4].DataType!.Value);
            Assert.Equal("1", data[4].CellValue!.Text);
        });
    }

    [Fact]
    public async Task Xlsx_NullCell_IsEmpty()
    {
        var cols = new[] { new ExportColumn("C", typeof(string)) };
        var rows = new object?[][] { new object?[] { null } };
        await WithExportedFile(new XlsxExporter(includeHeader: false), cols, rows, path =>
        {
            var data = ReadFirstSheetRows(path)[0];
            Assert.Null(data[0].CellValue);
            Assert.Null(data[0].InlineString);
        });
    }

    [Fact]
    public async Task Xlsx_Blob_IsPlaceholder()
    {
        var cols = new[] { new ExportColumn("C", typeof(byte[])) };
        var rows = new object?[][] { new object?[] { new byte[] { 1, 2, 3 } } };
        await WithExportedFile(new XlsxExporter(includeHeader: false), cols, rows, path =>
        {
            Assert.Equal(ExportValueFormatter.BlobPlaceholder, InlineText(ReadFirstSheetRows(path)[0][0]));
        });
    }

    [Fact]
    public async Task Xlsx_NoHeader_OmitsHeaderRow()
    {
        var rows = new object?[][] { new object?[] { "a", 1, 2m, new DateTime(2020, 1, 1), false } };
        await WithExportedFile(new XlsxExporter(includeHeader: false), Columns, rows, path =>
        {
            var sheetRows = ReadFirstSheetRows(path);
            Assert.Single(sheetRows);                    // no header row
            Assert.Equal("a", InlineText(sheetRows[0][0]));
        });
    }

    [Fact]
    public async Task Xlsx_EmptyResult_ProducesValidWorkbookWithOneSheet()
    {
        await WithExportedFile(new XlsxExporter(includeHeader: true), Columns, Array.Empty<object?[]>(), path =>
        {
            Assert.Equal(1, ReadSheetCount(path));
            Assert.Single(ReadFirstSheetRows(path)); // just the header
        });
    }

    [Fact]
    public async Task Xlsx_SplitsSheets_WhenExceedingPerSheetLimit()
    {
        // maxRowsPerSheet=3 with a header → 2 data rows per sheet; 5 rows → 3 sheets (2+2+1).
        var cols = new[] { new ExportColumn("N", typeof(int)) };
        var rows = Enumerable.Range(1, 5).Select(i => new object?[] { i }).ToArray();

        var path = TempPath();
        try
        {
            long written;
            await using (var sink = new FileExportSink(path, NoBom))
            {
                written = await new XlsxExporter(includeHeader: true, maxRowsPerSheet: 3)
                    .ExportAsync(cols, ToAsync(rows), sink, null, CancellationToken.None);
            }
            Assert.Equal(5, written);
            Assert.Equal(3, ReadSheetCount(path));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Service_ExportXlsxToFile_ProducesValidFile_AndRowCount()
    {
        var queryColumns = new[]
        {
            new QueryColumn("Name", typeof(string)),
            new QueryColumn("Qty", typeof(int)),
            new QueryColumn("Price", typeof(decimal)),
            new QueryColumn("When", typeof(DateTime)),
            new QueryColumn("Flag", typeof(bool)),
        };
        var row = new object?[][] { new object?[] { "a", 1, 2m, new DateTime(2021, 6, 1), true } };
        var source = new QueryResultExportSource(
            queryColumns, row, row, isPartial: false, streamAll: null, "test");

        var path = TempPath();
        try
        {
            var n = await new ExportService().ExportToFileAsync(
                source,
                new ExportRequest { Format = ExportFormat.Xlsx, Scope = ExportScope.AllRows, IncludeHeader = true },
                path, NoBom, null, CancellationToken.None);

            Assert.Equal(1, n);
            Assert.Equal(2, ReadFirstSheetRows(path).Count); // header + 1
        }
        finally { TryDelete(path); }
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static readonly Encoding NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static string? InlineText(Cell cell) => cell.InlineString?.Text?.Text;

    private static async Task WithExportedFile(
        XlsxExporter exporter, IReadOnlyList<ExportColumn> cols, IReadOnlyList<object?[]> rows, Action<string> assert)
    {
        var path = TempPath();
        try
        {
            await using (var sink = new FileExportSink(path, NoBom))
            {
                await exporter.ExportAsync(cols, ToAsync(rows), sink, null, CancellationToken.None);
            }
            assert(path);
        }
        finally { TryDelete(path); }
    }

    private static List<List<Cell>> ReadFirstSheetRows(string path)
    {
        using var doc = SpreadsheetDocument.Open(path, false);
        var wbPart = doc.WorkbookPart!;
        var firstSheet = wbPart.Workbook.Sheets!.Elements<Sheet>().First();
        var wsPart = (WorksheetPart)wbPart.GetPartById(firstSheet.Id!);
        var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>()!;
        return sheetData.Elements<Row>().Select(r => r.Elements<Cell>().ToList()).ToList();
    }

    private static int ReadSheetCount(string path)
    {
        using var doc = SpreadsheetDocument.Open(path, false);
        return doc.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Count();
    }

    private static async IAsyncEnumerable<object?[]> ToAsync(IReadOnlyList<object?[]> rows)
    {
        foreach (var r in rows) yield return r;
        await Task.CompletedTask;
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), "embertern-xlsx-" + Guid.NewGuid().ToString("N") + ".xlsx");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
