using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using EmberTern.Core.Metadata;
using EmberTern.Office;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap I10 — the legacy <c>.xls</c> (BIFF8) provider.
/// <para>
/// The cases mirror <see cref="XlsxImportProviderTests"/> deliberately: the two providers answer the same
/// questions about the same kind of content, so where they are ALLOWED to differ is exactly what these tests
/// pin. Two places they genuinely do — the row-number mechanism and the fact that a blank cell has its own slot
/// rather than being absent — are measured facts about the container, each recorded at its case.
/// </para>
/// <para>
/// ⚠ <b>What these fixtures do not prove.</b> They are written by NPOI, not by Excel, so they establish the
/// provider's behaviour and not Excel's output — I9's lesson stated plainly (a probe proves what it happened to
/// execute). The real workbook is covered where it belongs: section I of <c>tools/probes/DataImportRunProbe</c>
/// runs the user's own <c>.xls</c> through the whole production stack against a live engine.
/// </para>
/// </summary>
public class XlsImportProviderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "embertern-i10-" + Guid.NewGuid().ToString("N"));

    public XlsImportProviderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ── §0.6: the report has to name the row the user can find in their file ────────────────────────────

    /// <summary>
    /// ⭐ The container-specific fact, and the reason it could not simply be carried over from <c>.xlsx</c>.
    /// I0 measured that an OOXML sheet OMITS an empty row, which is why that provider must read the row's own
    /// declared index. BIFF does the opposite — the blank row is yielded — and the reader's <c>Depth</c> stays
    /// aligned with the sheet across it. Either way the number must be the SHEET's, never a counter of ours;
    /// this pins that it is, over a file with a hole in the middle.
    /// </summary>
    [Fact]
    public async Task RowNumbers_AreTheSheetsOwn_AcrossABlankRow()
    {
        var records = await ReadAllAsync(BuildTrapWorkbook());

        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7 }, records.Select(r => r.SourceRowNumber).ToArray());

        // Row 5 is blank; the row after it is still numbered 6, which is where the user sees its content.
        Assert.Equal("po pustym wierszu", records.Single(r => r.SourceRowNumber == 6).ValueAt(0));
    }

    /// <summary>A blank row survives as a record rather than vanishing — so a file with a hole in it produces
    /// something the user can see in the preview, not a silent absence. Its cells follow the user's declared
    /// <c>TreatEmptyAsNull</c> (default on), which is the same answer the .xlsx reader gives a blank cell: the
    /// question belongs to one owner, and it is not the reader.</summary>
    [Fact]
    public async Task ABlankRow_IsStillARecord()
    {
        var records = await ReadAllAsync(BuildTrapWorkbook());
        var blank = records.Single(r => r.SourceRowNumber == 5);

        var cells = blank.Values.ToArray();
        Assert.Equal(5, cells.Length);
        Assert.All(cells, value => Assert.Null(value));
    }

    /// <summary>With that behaviour turned off, the same cells come back as empty text instead — the reader
    /// obeys the setting rather than holding an opinion of its own.</summary>
    [Fact]
    public async Task TreatEmptyAsNullOff_GivesEmptyTextInstead()
    {
        var configuration = SpreadsheetConfiguration() with
        {
            Behavior = new ImportBehaviorOptions { TreatEmptyAsNull = false },
        };

        var records = await ReadAllAsync(BuildTrapWorkbook(), configuration);

        Assert.All(records.Single(r => r.SourceRowNumber == 5).Values.ToArray(),
            value => Assert.Equal(string.Empty, value));
    }

    // ── Values leave native (the pillar I8's type inference rests on) ───────────────────────────────────

    [Fact]
    public async Task TextNumberBooleanAndDate_AllLeaveAsTheirOwnCleanTypes()
    {
        var row = (await ReadAllAsync(BuildTrapWorkbook())).Single(r => r.SourceRowNumber == 2);

        Assert.Equal("WSPÓLNY", row.ValueAt(0));
        Assert.Equal(new DateTime(2023, 3, 15), row.ValueAt(1));
        Assert.Equal(11881d, row.ValueAt(2));
        Assert.Equal(true, row.ValueAt(3));
    }

    /// <summary>
    /// ⭐ The library decodes a date cell for us, so the danger is the opposite of the <c>.xlsx</c> one: not a
    /// date being missed, but a date being INVENTED where the format does not say so. The same number carrying
    /// no date format has to stay a number, or a column of money becomes a column of dates (§0.1).
    /// </summary>
    [Fact]
    public async Task ANumberWithoutADateFormat_StaysANumber()
    {
        var row = (await ReadAllAsync(BuildTrapWorkbook())).Single(r => r.SourceRowNumber == 2);
        Assert.Equal(11881d, row.ValueAt(2));
    }

    /// <summary>
    /// ⭐ Gotcha #268, carried into the second provider. The user's real custom currency format
    /// <c>#,##0\ [$€-1];[Red]\-#,##0\ [$€-1]</c> answers TRUE to a naive "does the code contain a d" test because
    /// <c>[Red]</c> contains one. Both providers ask <c>SpreadsheetNumberFormats</c>, which PARSES the code — so
    /// a money cell stays money here too, even though the library independently decided it was a date.
    /// </summary>
    [Fact]
    public async Task ACurrencyFormatWhoseCodeContainsALetterD_IsNotADate()
    {
        var row = (await ReadAllAsync(BuildTrapWorkbook())).Single(r => r.SourceRowNumber == 7);
        Assert.Equal(45000d, row.ValueAt(1));
    }

    /// <summary>Turning the option off must give back the serial the sheet actually stores — which is only
    /// possible because <c>ExcelSerialDate</c> owns the inverse of the same calendar the .xlsx reader uses
    /// forwards.</summary>
    [Fact]
    public async Task DatesAsDatesOff_GivesBackTheSerialNumber()
    {
        var configuration = SpreadsheetConfiguration() with
        {
            Spreadsheet = new SpreadsheetOptions { FirstDataRow = 2, DatesAsDates = false },
        };

        var records = await ReadAllAsync(BuildTrapWorkbook(), configuration);

        Assert.Equal(45000d, records.Single(r => r.SourceRowNumber == 2).ValueAt(1));
    }

    /// <summary>A pure time of day leaves as a <see cref="TimeSpan"/> — the same value the <c>.xlsx</c> provider
    /// yields for the same cell. The library hands it over as OLE's 1899-12-31 plus the time, so without the
    /// shared calendar the two providers would disagree about one workbook's content.</summary>
    [Fact]
    public async Task ATimeOnlyCell_LeavesAsATimeSpan_LikeTheXlsxProvider()
    {
        var row = (await ReadAllAsync(BuildTrapWorkbook())).Single(r => r.SourceRowNumber == 3);
        Assert.Equal(TimeSpan.FromHours(12), row.ValueAt(1));
    }

    /// <summary>
    /// ⭐ I9's epoch finding, pinned on the other provider. Excel's serial 15 is 1900-01-15 while
    /// <c>FromOADate(15)</c> is 1900-01-14, and a day lost in silence is §0.1's worst class.
    /// </summary>
    [Fact]
    public async Task ADateBeforeTheEpochsAgree_KeepsItsDay()
    {
        var row = (await ReadAllAsync(BuildTrapWorkbook())).Single(r => r.SourceRowNumber == 4);
        Assert.Equal(new DateTime(1900, 1, 15), row.ValueAt(1));
    }

    /// <summary>And the same value comes back out as serial 15, not 16 — the inverse has to agree with the
    /// forward direction or a round trip through the option silently shifts the date.</summary>
    [Fact]
    public async Task TheSerialRoundTrip_AgreesWithTheForwardDirection()
    {
        var configuration = SpreadsheetConfiguration() with
        {
            Spreadsheet = new SpreadsheetOptions { FirstDataRow = 2, DatesAsDates = false },
        };

        var records = await ReadAllAsync(BuildTrapWorkbook(), configuration);

        Assert.Equal(15d, records.Single(r => r.SourceRowNumber == 4).ValueAt(1));
    }

    // ── R20: an error cell is never data ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ R20 on the second source. <c>#N/A</c> must not reach a VARCHAR as the text "#N/A" — and the marker is
    /// deliberately the SAME source-neutral <c>SourceErrorValue</c> the .xlsx provider raises, so the converter
    /// needed no branch for this format at all.
    /// </summary>
    [Fact]
    public async Task AnErrorCell_IsRefused_EvenByATextColumn()
    {
        var records = await ReadAllAsync(BuildTrapWorkbook());
        var value = records.Single(r => r.SourceRowNumber == 3).ValueAt(4);

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

        Assert.Null(records.Single(r => r.SourceRowNumber == 3).ValueAt(4));
    }

    // ── Schema, sheets and the row window ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Schema_TakesItsNamesFromTheHeaderRow()
    {
        var schema = await new XlsImportProvider().ReadSchemaAsync(
            new FileImportSource(BuildTrapWorkbook()), SpreadsheetConfiguration(), CancellationToken.None);

        Assert.Equal(new[] { "Kod", "Data", "Ilosc", "Flaga", "Uwaga" },
            schema.Fields.Select(f => f.Name).ToArray());
        Assert.True(schema.HasHeader);
    }

    /// <summary>A column the header forgot to name still has to be mappable, or the data in it is unreachable —
    /// the same guarantee the .xlsx provider gives by taking the widest sampled row.</summary>
    [Fact]
    public async Task AColumnTheHeaderDoesNotName_IsStillOffered()
    {
        var schema = await new XlsImportProvider().ReadSchemaAsync(
            new FileImportSource(BuildWorkbookWithShortHeader()), SpreadsheetConfiguration(), CancellationToken.None);

        Assert.Equal(3, schema.Fields.Count);
        Assert.Equal("Kod", schema.Fields[0].Name);
        Assert.False(schema.Fields[2].HasRealName);
        Assert.Equal("C", schema.Fields[2].Name); // the column letter the user sees in Excel
    }

    [Fact]
    public async Task ListSheets_NamesEverySheet_AndCanCountTheirRows()
    {
        var sheets = await new XlsImportProvider()
            .ListSheetsAsync(new FileImportSource(BuildTrapWorkbook()), CancellationToken.None);

        Assert.Equal(2, sheets.Count);
        Assert.Equal("Arkusz1", sheets[0].Name);
        Assert.Equal("Drugi", sheets[1].Name);
        Assert.Equal(0, sheets[0].Index);
    }

    /// <summary>The sheet the user picked is the sheet that is read — the picker would be decoration
    /// otherwise.</summary>
    [Fact]
    public async Task TheChosenSheet_IsTheOneRead()
    {
        var configuration = SpreadsheetConfiguration() with
        {
            Spreadsheet = new SpreadsheetOptions { SheetIndex = 1, FirstDataRow = 1, HasHeader = false },
        };

        var records = await ReadAllAsync(BuildTrapWorkbook(), configuration);

        Assert.Equal("drugi arkusz", Assert.Single(records).ValueAt(0));
    }

    [Fact]
    public async Task TheRowWindow_IsAppliedOnSheetRowNumbers()
    {
        var configuration = SpreadsheetConfiguration() with
        {
            Spreadsheet = new SpreadsheetOptions { FirstDataRow = 3, LastRow = 4 },
        };

        var records = await ReadAllAsync(BuildTrapWorkbook(), configuration);

        Assert.Equal(new[] { 3, 4 }, records.Select(r => r.SourceRowNumber).ToArray());
    }

    // ── The refusal (§0: an honest "no", never a pretence) ──────────────────────────────────────────────

    /// <summary>
    /// ⭐ The mirror of the .xlsx provider's refusal. Handed a real OOXML package the library says
    /// <c>Invalid file signature</c>, which tells the user nothing; the provider turns it into the sentence that
    /// names the actual situation and the cheap way out of it.
    /// </summary>
    [Fact]
    public async Task AnOoxmlFileUnderAnXlsName_IsRefusedWithAnActionableMessage()
    {
        var path = Path.Combine(_directory, "przebrany.xls");
        await File.WriteAllBytesAsync(path, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0 }); // a ZIP header

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => new XlsImportProvider().ReadSchemaAsync(
                new FileImportSource(path), SpreadsheetConfiguration(), CancellationToken.None));

        Assert.Contains("przebrany.xls", error.Message, StringComparison.Ordinal);
        Assert.Contains(".xlsx", error.Message, StringComparison.Ordinal);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────

    private static ColumnSpec TextColumn() => new("UWAGA", "VARCHAR(100)");

    private static ImportConfiguration SpreadsheetConfiguration() => ImportConfiguration.Empty with
    {
        Source = SourceDescriptor.File(ImportSourceKind.Xls, "trap.xls"),
        Delimited = null,
        Spreadsheet = new SpreadsheetOptions { FirstDataRow = 2 },
    };

    private static async Task<List<RawRecord>> ReadAllAsync(
        string path, ImportConfiguration? configuration = null)
    {
        var records = new List<RawRecord>();
        var source = new FileImportSource(path);

        await foreach (var record in new XlsImportProvider().ReadRecordsAsync(
            source, configuration ?? SpreadsheetConfiguration(), CancellationToken.None))
        {
            records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// The fixture, laid out so each row carries exactly one question:
    /// <list type="bullet">
    /// <item>row 1 — the header</item>
    /// <item>row 2 — text / a date / a plain number / a boolean</item>
    /// <item>row 3 — a pure time of day, and an error cell</item>
    /// <item>row 4 — a date inside the range where Excel's epoch and OLE's disagree</item>
    /// <item>row 5 — left out entirely (the blank-row case)</item>
    /// <item>row 6 — content after the hole, to show its number did not shift</item>
    /// <item>row 7 — a currency format whose code contains a letter <c>d</c> (gotcha #268)</item>
    /// </list>
    /// </summary>
    private string BuildTrapWorkbook()
    {
        var path = Path.Combine(_directory, "trap.xls");

        var workbook = new HSSFWorkbook();
        var sheet = workbook.CreateSheet("Arkusz1");
        workbook.CreateSheet("Drugi").CreateRow(0).CreateCell(0).SetCellValue("drugi arkusz");

        var format = workbook.CreateDataFormat();
        var dateStyle = workbook.CreateCellStyle();
        dateStyle.DataFormat = format.GetFormat("yyyy-mm-dd");
        var timeStyle = workbook.CreateCellStyle();
        timeStyle.DataFormat = format.GetFormat("hh:mm:ss");
        var currencyStyle = workbook.CreateCellStyle();
        currencyStyle.DataFormat = format.GetFormat("#,##0\\ [$€-1];[Red]\\-#,##0\\ [$€-1]");

        var header = sheet.CreateRow(0);
        foreach (var (name, i) in new[] { "Kod", "Data", "Ilosc", "Flaga", "Uwaga" }.Select((n, i) => (n, i)))
        {
            header.CreateCell(i).SetCellValue(name);
        }

        var row2 = sheet.CreateRow(1);
        row2.CreateCell(0).SetCellValue("WSPÓLNY");
        Styled(row2, 1, 45000d, dateStyle);       // 2023-03-15
        row2.CreateCell(2).SetCellValue(11881d);  // the same shape, no date format ⇒ a number
        row2.CreateCell(3).SetCellValue(true);
        row2.CreateCell(4).SetCellValue("bez uwag");

        var row3 = sheet.CreateRow(2);
        row3.CreateCell(0).SetCellValue("CZAS");
        Styled(row3, 1, 0.5d, timeStyle);         // 12:00:00, no date part
        row3.CreateCell(2).SetCellValue(1d);
        row3.CreateCell(3).SetCellValue(false);
        row3.CreateCell(4).SetCellErrorValue(42); // #N/A

        var row4 = sheet.CreateRow(3);
        row4.CreateCell(0).SetCellValue("STARA DATA");
        Styled(row4, 1, 15d, dateStyle);          // 1900-01-15 in Excel, 1900-01-14 via FromOADate
        row4.CreateCell(2).SetCellValue(2d);
        row4.CreateCell(3).SetCellValue(false);
        row4.CreateCell(4).SetCellValue("stara");

        // Sheet row 5 is deliberately never created.

        var row6 = sheet.CreateRow(5);
        row6.CreateCell(0).SetCellValue("po pustym wierszu");
        row6.CreateCell(4).SetCellValue("ok");

        var row7 = sheet.CreateRow(6);
        row7.CreateCell(0).SetCellValue("WALUTA");
        Styled(row7, 1, 45000d, currencyStyle);   // money, not a date — gotcha #268
        row7.CreateCell(4).SetCellValue("waluta");

        using var output = File.Create(path);
        workbook.Write(output, leaveOpen: true);
        return path;
    }

    /// <summary>A sheet whose header names two columns but whose data reaches three.</summary>
    private string BuildWorkbookWithShortHeader()
    {
        var path = Path.Combine(_directory, "short-header.xls");

        var workbook = new HSSFWorkbook();
        var sheet = workbook.CreateSheet("Arkusz1");

        var header = sheet.CreateRow(0);
        header.CreateCell(0).SetCellValue("Kod");
        header.CreateCell(1).SetCellValue("Nazwa");

        var row = sheet.CreateRow(1);
        row.CreateCell(0).SetCellValue("A");
        row.CreateCell(1).SetCellValue("B");
        row.CreateCell(2).SetCellValue("bez nagłówka");

        using var output = File.Create(path);
        workbook.Write(output, leaveOpen: true);
        return path;
    }

    private static void Styled(IRow row, int column, double value, ICellStyle style)
    {
        var cell = row.CreateCell(column);
        cell.SetCellValue(value);
        cell.CellStyle = style;
    }
}
