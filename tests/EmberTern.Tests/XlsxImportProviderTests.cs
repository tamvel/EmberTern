using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using EmberTern.Core.Metadata;
using EmberTern.Office;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap I9 — the XLSX provider, pinned against the constructs I0 measured in a real workbook.
/// <para>
/// Every case here exists because a measurement said it could go wrong, not because it was easy to write: the
/// missing middle cell and the row-index gap are the two silent-corruption vectors I0 named (findings §3.2), and
/// the currency-format case is a defect found in I0's own probe heuristic while implementing this etap.
/// </para>
/// </summary>
public class XlsxImportProviderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "embertern-i9-" + Guid.NewGuid().ToString("N"));

    public XlsxImportProviderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ── The two silent-corruption vectors (I0 §3.2) ─────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ X4. A row whose middle cell is ABSENT (not blank) must keep every later value in its own column. A
    /// positional reader would shift "po luce" into column B and every column after it — with no error at all.
    /// </summary>
    [Fact]
    public async Task MissingMiddleCell_LeavesAGap_AndDoesNotShiftTheRestOfTheRow()
    {
        var records = await ReadAllAsync(BuildTrapWorkbook());

        var ragged = records.Single(r => r.SourceRowNumber == 3);
        Assert.Equal("przed luką", ragged.ValueAt(0));
        Assert.Null(ragged.ValueAt(1));            // column B is absent — a gap, not the next value
        Assert.Equal("po luce", ragged.ValueAt(2)); // still in C, where the user put it
    }

    /// <summary>
    /// ⭐ X5. Empty rows are absent from the file, so the row number must come from the worksheet's own
    /// RowIndex. A running counter would report row 8 for what the user sees as row 10 — i.e. the error report
    /// would point at the wrong line (§0.6).
    /// </summary>
    [Fact]
    public async Task RowNumbers_ComeFromTheWorksheet_NotFromACounter()
    {
        var records = await ReadAllAsync(BuildTrapWorkbook());

        Assert.Equal(new[] { 2, 3, 4, 5, 6, 10 }, records.Select(r => r.SourceRowNumber).ToArray());
        Assert.Equal("po dwóch pustych wierszach", records.Single(r => r.SourceRowNumber == 10).ValueAt(0));
    }

    // ── Values leave native (the pillar I8's type inference rests on) ───────────────────────────────────

    [Fact]
    public async Task SharedStringsAndInlineStrings_BothResolveToTheirText()
    {
        var records = await ReadAllAsync(BuildTrapWorkbook());
        var row = records.Single(r => r.SourceRowNumber == 2);

        Assert.Equal("WSPÓLNY", row.ValueAt(0));  // shared string, stored as an index
        Assert.Equal("WBUDOWANY", row.ValueAt(4)); // inline string, whose CellValue is NULL
    }

    [Fact]
    public async Task ANumberWithADateFormat_BecomesADate_AndAPlainNumberStaysANumber()
    {
        var records = await ReadAllAsync(BuildTrapWorkbook());
        var row = records.Single(r => r.SourceRowNumber == 2);

        Assert.Equal(new DateTime(2023, 3, 15), row.ValueAt(1)); // serial 45000 + built-in format 14
        Assert.Equal(11881d, row.ValueAt(2));                    // same shape, no date format ⇒ a number
    }

    [Fact]
    public async Task ABooleanCell_BecomesABoolean_AndAFormulaContributesItsCachedValue()
    {
        var records = await ReadAllAsync(BuildTrapWorkbook());

        Assert.Equal(true, records.Single(r => r.SourceRowNumber == 2).ValueAt(3));
        Assert.Equal(3d, records.Single(r => r.SourceRowNumber == 4).ValueAt(0)); // =1+2, cached as 3
    }

    [Fact]
    public async Task DatesAsDatesOff_LeavesTheSerialNumberAlone()
    {
        var configuration = SpreadsheetConfiguration() with
        {
            Spreadsheet = new SpreadsheetOptions { FirstDataRow = 2, DatesAsDates = false },
        };

        var records = await ReadAllAsync(BuildTrapWorkbook(), configuration);

        Assert.Equal(45000d, records.Single(r => r.SourceRowNumber == 2).ValueAt(1));
    }

    // ── R20: an error cell is never data ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ R20. <c>#N/A</c> must not reach a VARCHAR as the text "#N/A". The provider marks it, and the converter
    /// refuses it for EVERY target type — which is the half that makes the guarantee real, since the text branch
    /// accepts anything.
    /// </summary>
    [Fact]
    public async Task AnErrorCell_IsRefused_EvenByATextColumn()
    {
        var records = await ReadAllAsync(BuildTrapWorkbook());
        var value = records.Single(r => r.SourceRowNumber == 5).ValueAt(0);

        var marked = Assert.IsType<SourceErrorValue>(value);
        Assert.Equal("#N/A", marked.Code);

        var converted = ImportValueConverter.Convert(
            marked, ImportTargetType.Resolve(TextColumn()), new ImportCultureOptions());

        Assert.False(converted.IsSuccess);
        Assert.Equal(ImportErrorKind.SourceErrorValue, converted.Kind);
    }

    [Fact]
    public async Task ExcelErrorCellsAsNull_TakesTheCellAsNullInstead()
    {
        var configuration = SpreadsheetConfiguration() with
        {
            Behavior = new ImportBehaviorOptions { ExcelErrorCellsAsNull = true },
        };

        var records = await ReadAllAsync(BuildTrapWorkbook(), configuration);

        Assert.Null(records.Single(r => r.SourceRowNumber == 5).ValueAt(0));
    }

    // ── Schema, sheets and the dimension hint ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Schema_TakesItsNamesFromTheHeaderRow_AndItsWidthFromTheWidestRow()
    {
        var path = BuildTrapWorkbook();
        var schema = await new XlsxImportProvider()
            .ReadSchemaAsync(new FileImportSource(path), SpreadsheetConfiguration(), CancellationToken.None);

        Assert.Equal(new[] { "Kod", "Data", "Ilosc", "Flaga", "Uwaga" },
            schema.Fields.Select(f => f.Name).ToArray());
        Assert.True(schema.HasHeader);
    }

    [Fact]
    public async Task ListSheets_NamesEverySheet()
    {
        var path = BuildTrapWorkbook();
        var sheets = await new XlsxImportProvider()
            .ListSheetsAsync(new FileImportSource(path), CancellationToken.None);

        var sheet = Assert.Single(sheets);
        Assert.Equal(0, sheet.Index);
        Assert.Equal("Arkusz1", sheet.Name);
    }

    /// <summary>The row window is the user's statement about the file, and it is honoured on the worksheet's own
    /// numbering — so "last row 4" means the row the user sees as 4.</summary>
    [Fact]
    public async Task TheRowWindow_IsAppliedOnWorksheetRowNumbers()
    {
        var configuration = SpreadsheetConfiguration() with
        {
            Spreadsheet = new SpreadsheetOptions { FirstDataRow = 3, LastRow = 5 },
        };

        var records = await ReadAllAsync(BuildTrapWorkbook(), configuration);

        Assert.Equal(new[] { 3, 4, 5 }, records.Select(r => r.SourceRowNumber).ToArray());
    }

    // ── The refusal (§0: an honest "no", never a pretence) ──────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>Added in etap C8, and its absence was itself a finding.</b> The <c>.xls</c> provider's mirror of
    /// this refusal has been pinned since I10; this one — the case I0 §3.5 found on the machine's REAL
    /// spreadsheets, an old BIFF workbook still carrying an <c>.xlsx</c> name — had no test at all. So of the
    /// two sentences C8 migrated, one had a pin and one had none, and the asymmetry sat in the more common
    /// case.
    ///
    /// <para>⚠ Why it matters beyond symmetry: the library's own answer here is
    /// <c>File contains corrupted data</c>, which is not merely unhelpful but <b>false</b> — the file is
    /// intact. This test is what stops that answer reaching the user again.</para>
    /// </summary>
    [Fact]
    public async Task ABiffFileUnderAnXlsxName_IsRefusedWithAnActionableMessage()
    {
        var path = Path.Combine(_directory, "przebrany.xlsx");
        // The OLE2 compound-document signature every BIFF workbook starts with — i.e. a real .xls.
        await File.WriteAllBytesAsync(
            path, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 });

        var error = await Assert.ThrowsAsync<ImportSourceException>(
            () => new XlsxImportProvider().ReadSchemaAsync(
                new FileImportSource(path), SpreadsheetConfiguration(), CancellationToken.None));

        Assert.Contains("przebrany.xlsx", error.Message, StringComparison.Ordinal);
        Assert.Contains(".xls", error.Message, StringComparison.Ordinal);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────

    private static ColumnSpec TextColumn() => new("UWAGA", "VARCHAR(100)");

    private static ImportConfiguration SpreadsheetConfiguration() => ImportConfiguration.Empty with
    {
        Source = SourceDescriptor.File(ImportSourceKind.Xlsx, "trap.xlsx"),
        Delimited = null,
        Spreadsheet = new SpreadsheetOptions { FirstDataRow = 2 },
    };

    private async Task<List<RawRecord>> ReadAllAsync(string path, ImportConfiguration? configuration = null)
    {
        var records = new List<RawRecord>();
        await foreach (var record in new XlsxImportProvider().ReadRecordsAsync(
            new FileImportSource(path), configuration ?? SpreadsheetConfiguration(), CancellationToken.None))
        {
            records.Add(record);
        }
        return records;
    }

    /// <summary>
    /// A workbook carrying every construct I0 measured as a trap, in one sheet:
    /// shared + inline strings, a date by built-in format, a number that looks identical but has none, a
    /// boolean, a formula's cached value, an error cell, a MISSING middle cell, a row-index gap, and a custom
    /// CURRENCY format whose <c>[Red]</c> section must not be mistaken for a date.
    /// </summary>
    private string BuildTrapWorkbook()
    {
        var path = Path.Combine(_directory, "trap.xlsx");
        if (File.Exists(path)) return path;

        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var sharedStrings = workbookPart.AddNewPart<SharedStringTablePart>();
        sharedStrings.SharedStringTable = new SharedStringTable(
            new SharedStringItem(new Text("Kod")),
            new SharedStringItem(new Text("Data")),
            new SharedStringItem(new Text("WSPÓLNY")));

        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = new Stylesheet(
            new NumberingFormats(
                // The real user file's own custom format. Currency — and its [Red] section contains a 'd'.
                new NumberingFormat
                {
                    NumberFormatId = 164,
                    FormatCode = "#,##0\\ [$€-1];[Red]\\-#,##0\\ [$€-1]",
                }),
            new Fonts(new Font()),
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None })),
            new Borders(new Border()),
            new CellStyleFormats(new CellFormat()),
            new CellFormats(
                new CellFormat(),                                                   // 0 — General
                new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true },   // 1 — built-in date
                new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true })); // 2 — custom CURRENCY

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();

        sheetData.Append(Row(1,
            Cell("A1", CellValues.SharedString, "0"),
            Cell("B1", CellValues.SharedString, "1"),
            Cell("C1", CellValues.InlineString, "Ilosc"),
            Cell("D1", CellValues.InlineString, "Flaga"),
            Cell("E1", CellValues.InlineString, "Uwaga")));

        sheetData.Append(Row(2,
            Cell("A2", CellValues.SharedString, "2"),      // shared string
            Cell("B2", null, "45000", style: 1),           // number + DATE format ⇒ 2023-03-15
            Cell("C2", null, "11881"),                     // number, no format ⇒ stays a number
            Cell("D2", CellValues.Boolean, "1"),
            Cell("E2", CellValues.InlineString, "WBUDOWANY")));

        // THE TRAP: column B is absent entirely.
        sheetData.Append(Row(3,
            Cell("A3", CellValues.InlineString, "przed luką"),
            Cell("C3", CellValues.InlineString, "po luce")));

        var formula = new Cell
        {
            CellReference = "A4",
            CellFormula = new CellFormula("1+2"),
            CellValue = new CellValue("3"),
        };
        sheetData.Append(Row(4, formula));

        sheetData.Append(Row(5, Cell("A5", CellValues.Error, "#N/A")));

        // A CURRENCY cell. Must stay a number: its format's [Red] must not read as a date token.
        sheetData.Append(Row(6, Cell("A6", null, "1234.56", style: 2)));

        // Rows 7–9 are ABSENT.
        sheetData.Append(Row(10, Cell("A10", CellValues.InlineString, "po dwóch pustych wierszach")));

        worksheetPart.Worksheet = new Worksheet(sheetData);
        workbookPart.Workbook.AppendChild(new Sheets(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1U,
            Name = "Arkusz1",
        }));
        workbookPart.Workbook.Save();

        return path;
    }

    /// <summary>⭐ The currency cell from <see cref="BuildTrapWorkbook"/>. This is the case the I0 probe's own
    /// three-line heuristic gets wrong — <c>[Red]</c> contains a <c>d</c> — which would have turned a money
    /// column into dates.</summary>
    [Fact]
    public async Task ACurrencyFormatWhoseCodeContainsColourMarkup_IsNotMistakenForADate()
    {
        var records = await ReadAllAsync(BuildTrapWorkbook());

        Assert.Equal(1234.56d, records.Single(r => r.SourceRowNumber == 6).ValueAt(0));
    }

    private static Row Row(uint index, params Cell[] cells)
    {
        var row = new Row { RowIndex = index };
        foreach (var cell in cells) row.Append(cell);
        return row;
    }

    private static Cell Cell(string reference, CellValues? type, string value, uint style = 0)
    {
        var cell = new Cell { CellReference = reference, StyleIndex = style };
        if (type == CellValues.InlineString)
        {
            cell.DataType = CellValues.InlineString;
            cell.InlineString = new InlineString(new Text(value));
        }
        else
        {
            if (type is not null) cell.DataType = type;
            cell.CellValue = new CellValue(value);
        }
        return cell;
    }
}
