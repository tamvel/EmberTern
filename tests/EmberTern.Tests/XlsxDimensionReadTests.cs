using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using EmberTern.Office;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// M3b.1a — <c>XlsxImportProvider.RowsFromDimension</c> reads <c>&lt;dimension&gt;</c> with the SAX reader
/// instead of through <c>worksheetPart.Worksheet</c>, which materialized the ENTIRE worksheet DOM to fetch one
/// attribute (measured: 8 546 ms vs 15 ms on a 300 000-row workbook).
///
/// <para>⚠⚠ These are the FIRST unit tests for this provider — it had none, and was verified only by the live
/// probes. That is exactly why the fix needed them: the value it returns is what the import surface shows as the
/// row estimate and what drives the progress bar's percentage mode, so a silent change there would surface far
/// from its cause.</para>
///
/// <para>⚠ The workbooks are written with <c>OpenXmlWriter</c> so a test can control whether
/// <c>&lt;dimension&gt;</c> is present at all — the distinction the fix turns on, and one no fixture file could
/// express as clearly.</para>
/// </summary>
public class XlsxDimensionReadTests : IDisposable
{
    private readonly string _dir;

    public XlsxDimensionReadTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "embertern-xlsx-dim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static ImportConfiguration Config(string path) => new()
    {
        Source = new SourceDescriptor { Kind = ImportSourceKind.Xlsx, Path = path },
        Delimited = null,
        Spreadsheet = new SpreadsheetOptions { HasHeader = true },
    };

    [Fact]
    public async Task DeclaredDimension_IsReported_AsTheRowEstimate()
    {
        var path = Write("with-dim.xlsx", rows: 12, withDimension: true);

        var schema = await new XlsxImportProvider()
            .ReadSchemaAsync(new FileImportSource(path), Config(path), CancellationToken.None);

        // 12 wierszy danych + nagłówek — dokładnie to, co deklaruje <dimension>.
        Assert.Equal(13L, schema.EstimatedRows);
        Assert.Equal(3, schema.Fields.Count);
    }

    /// <summary>
    /// ⭐ A workbook with no <c>&lt;dimension&gt;</c> must report <c>null</c> — the element is a HINT (REK-6 (6)),
    /// so its absence is "unknown", never a counted or invented number. The import surface then runs its progress
    /// indeterminate instead of claiming a percentage it cannot know.
    /// </summary>
    [Fact]
    public async Task MissingDimension_IsUnknown_NotAnInventedNumber()
    {
        var path = Write("no-dim.xlsx", rows: 12, withDimension: false);

        var schema = await new XlsxImportProvider()
            .ReadSchemaAsync(new FileImportSource(path), Config(path), CancellationToken.None);

        Assert.Null(schema.EstimatedRows);
        Assert.Equal(3, schema.Fields.Count);   // reszta schematu czytana normalnie
    }

    [Fact]
    public async Task ListSheets_ReportsTheSameEstimate_AsTheSchema()
    {
        var path = Write("sheets.xlsx", rows: 7, withDimension: true);
        var provider = new XlsxImportProvider();
        var source = new FileImportSource(path);

        var sheets = await provider.ListSheetsAsync(source, CancellationToken.None);
        var schema = await provider.ReadSchemaAsync(source, Config(path), CancellationToken.None);

        // Ta sama liczba z dwóch wywołań tej samej metody — gdyby jedno z nich czytało inaczej, rozjazd
        // pokazałby się użytkownikowi jako inna liczba w selektorze arkusza niż w podglądzie.
        Assert.Equal(8L, Assert.Single(sheets).EstimatedRows);
        Assert.Equal(8L, schema.EstimatedRows);
    }

    /// <summary>
    /// ⛔⛔ THE GUARD THAT MATTERS MOST, and it reads the SOURCE on purpose.
    /// <para>
    /// <c>worksheetPart.Worksheet?.SheetDimension?.Reference?.Value</c> is the obvious one-liner, it returns the
    /// same answer as the SAX read, and it costs three orders of magnitude more. Every behavioural test above
    /// stays green with it restored — so no assertion about the VALUE can protect this. The only thing that
    /// distinguishes the two is which mechanism the code uses.
    /// </para>
    /// <para>⚠ Verified by planting the violation: with the DOM property back, this test fails and the three
    /// above still pass.</para>
    /// </summary>
    [Fact]
    public void RowsFromDimension_UsesTheSaxReader_NeverTheWorksheetDom()
    {
        var source = ReadProviderSource();
        var start = source.IndexOf("private static long? RowsFromDimension", StringComparison.Ordinal);
        Assert.True(start > 0, "nie znaleziono RowsFromDimension — czy metoda została przeniesiona?");

        // Ciało metody: od jej nagłówka do następnej deklaracji na tym samym poziomie.
        var end = source.IndexOf("private static string? AsHeaderText", start, StringComparison.Ordinal);
        Assert.True(end > start, "nie znaleziono końca RowsFromDimension");
        var body = source[start..end];

        Assert.DoesNotContain(".Worksheet", body, StringComparison.Ordinal);
        Assert.Contains("OpenXmlReader.Create", body, StringComparison.Ordinal);

        // ⚠ Zatrzymanie na <sheetData> jest tym, co czyni odczyt tanim także dla pliku BEZ atrybutu; bez niego
        // czytelnik przeszedłby przez wszystkie wiersze i zamienilibyśmy jeden drogi mechanizm na drugi.
        Assert.Contains("typeof(SheetData)", body, StringComparison.Ordinal);
    }

    private static string ReadProviderSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "src", "EmberTern.Office", "XlsxImportProvider.cs");
        Assert.True(File.Exists(path), $"nie znaleziono {path}");
        return File.ReadAllText(path);
    }

    private string Write(string name, int rows, bool withDimension)
    {
        var path = Path.Combine(_dir, name);
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = doc.AddWorkbookPart();
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

        using (var w = OpenXmlWriter.Create(worksheetPart))
        {
            w.WriteStartElement(new Worksheet());
            if (withDimension)
            {
                w.WriteElement(new SheetDimension
                {
                    Reference = "A1:C" + (rows + 1).ToString(CultureInfo.InvariantCulture),
                });
            }
            w.WriteStartElement(new SheetData());

            WriteRow(w, 1, "KOD", "NAZWA", "KWOTA");
            for (var i = 1; i <= rows; i++)
            {
                WriteRow(w, i + 1,
                    "K" + i.ToString(CultureInfo.InvariantCulture),
                    "Pozycja " + i.ToString(CultureInfo.InvariantCulture),
                    i.ToString(CultureInfo.InvariantCulture));
            }

            w.WriteEndElement();
            w.WriteEndElement();
        }

        workbookPart.Workbook = new Workbook(
            new Sheets(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Dane",
            }));
        workbookPart.Workbook.Save();
        return path;
    }

    private static void WriteRow(OpenXmlWriter w, int index, params string[] values)
    {
        w.WriteStartElement(new Row { RowIndex = (uint)index });
        for (var c = 0; c < values.Length; c++)
        {
            w.WriteElement(new Cell
            {
                CellReference = (char)('A' + c) + index.ToString(CultureInfo.InvariantCulture),
                DataType = CellValues.String,
                CellValue = new CellValue(values[c]),
            });
        }
        w.WriteEndElement();
    }
}
