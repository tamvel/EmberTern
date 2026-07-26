using System.Diagnostics;
using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

// DEVELOPER VERIFICATION TOOL — NOT PART OF THE PRODUCT. See tools/probes/README.md.
//
// Data Import — etap I0. See the .csproj header for what each phase (G/X / R / F / D2) answers and why.
//
//   dotnet run --project tools\probes\DataImportXlsxProbe
//
// No server and no password: this measures a FILE FORMAT and a library, not the engine.

Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

const string CasesPath = @"C:\Temp\et_import_probe_cases.xlsx";
const string BigPath = @"C:\Temp\et_import_probe_big.xlsx";
const int BigRows = 100_000;

int failures = 0;
void Fail(string what, string detail) { failures++; Console.WriteLine($"  FAIL  {what} — {detail}"); }
void Pass(string what, string detail) => Console.WriteLine($"  PASS  {what} — {detail}");
void Info(string what, string detail) => Console.WriteLine($"  ....  {what} — {detail}");
void Section(string title) => Console.WriteLine($"\n=== {title} " + new string('=', Math.Max(0, 74 - title.Length)));

Console.WriteLine("Data Import — I0 xlsx-reading probe (DocumentFormat.OpenXml 3.1.0)");

try
{
    // ── Phase G — generate the cases workbook ────────────────────────────────────────────────────────
    Section("Phase G — generate a workbook carrying every trap");
    BuildCasesWorkbook(CasesPath);
    Info("G  cases workbook", $"{CasesPath} ({new FileInfo(CasesPath).Length:N0} B)");

    // ── Phase X — what does each trap look like to a reader? ─────────────────────────────────────────
    Section("Phase X — how each construct presents itself");
    {
        using var doc = SpreadsheetDocument.Open(CasesPath, false);
        var wbPart = doc.WorkbookPart!;
        var sheets = wbPart.Workbook.Sheets!.Elements<Sheet>().ToList();
        Info("X0  sheets", string.Join(" · ", sheets.Select((s, i) => $"[{i}] {s.Name}")));

        var wsPart = (WorksheetPart)wbPart.GetPartById(sheets[0].Id!.Value!);
        var dim = wsPart.Worksheet.SheetDimension?.Reference?.Value;
        Info("X1  SheetDimension", dim is null ? "ABSENT — a reader cannot rely on it for the last row" : $"'{dim}' present");

        var shared = wbPart.SharedStringTablePart?.SharedStringTable;
        Info("X2  shared string table", shared is null
            ? "ABSENT"
            : $"{shared.Elements<SharedStringItem>().Count()} distinct items — a SharedString cell's value is an INDEX into this");

        var styles = wbPart.WorkbookStylesPart!.Stylesheet;
        var cellFormats = styles.CellFormats!.Elements<CellFormat>().ToList();
        var customFormats = styles.NumberingFormats?.Elements<NumberingFormat>().ToList() ?? new List<NumberingFormat>();
        Info("X3  cell formats", $"{cellFormats.Count} xf entries; custom numFmts: " +
            (customFormats.Count == 0 ? "none" : string.Join(", ", customFormats.Select(n => $"{n.NumberFormatId}='{n.FormatCode}'"))));

        string Describe(Cell c)
        {
            var raw = c.CellValue?.Text ?? "(no CellValue)";
            var type = KindName(c);
            uint styleIdx = c.StyleIndex?.Value ?? 0;
            uint numFmtId = styleIdx < cellFormats.Count ? cellFormats[(int)styleIdx].NumberFormatId?.Value ?? 0u : 0u;
            var fmtCode = customFormats.FirstOrDefault(n => n.NumberFormatId?.Value == numFmtId)?.FormatCode?.Value;
            var resolved = "";

            if (c.DataType?.Value == CellValues.SharedString && shared is not null && int.TryParse(raw, out var ssi))
                resolved = $" ⇒ shared[{ssi}] = '{shared.Elements<SharedStringItem>().ElementAt(ssi).InnerText}'";
            else if (c.DataType?.Value == CellValues.InlineString)
                resolved = $" ⇒ inline = '{c.InlineString?.Text?.Text}'";
            else if (IsDateFormat(numFmtId, fmtCode) && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial))
                resolved = $" ⇒ DATE (numFmt {numFmtId}{(fmtCode is null ? "" : $" '{fmtCode}'")}) = {DateTime.FromOADate(serial):yyyy-MM-dd}";

            var formula = c.CellFormula is null ? "" : $" [formula '{c.CellFormula.Text}' + cached value]";
            return $"{c.CellReference,-4} type={type,-13} raw='{raw}' style={styleIdx} numFmt={numFmtId}{formula}{resolved}";
        }

        foreach (var row in wsPart.Worksheet.Descendants<Row>())
        {
            var cells = row.Elements<Cell>().ToList();
            Console.WriteLine($"    row {row.RowIndex?.Value,-3} cells={cells.Count}");
            foreach (var c in cells) Console.WriteLine("        " + Describe(c));
        }

        // The empty-cell trap — row 3 deliberately omits B.
        var row3 = wsPart.Worksheet.Descendants<Row>().First(r => r.RowIndex?.Value == 3);
        var refs = row3.Elements<Cell>().Select(c => c.CellReference?.Value ?? "?").ToList();
        if (refs.Count == 2 && refs[0].StartsWith("A", StringComparison.Ordinal) && refs[1].StartsWith("C", StringComparison.Ordinal))
            Pass("X4  missing middle cell", $"row 3 yields [{string.Join(", ", refs)}] — B IS ABSENT, not empty. " +
                "A positional reader would shift C's value into column B ⇒ silent column shift (§0.1). " +
                "The provider MUST place values by CellReference.");
        else
            Info("X4  missing middle cell", $"row 3 yields [{string.Join(", ", refs)}] — unexpected shape, re-check the generator");

        // Row-index gap — rows 8 and 9 are absent.
        var rowIdx = wsPart.Worksheet.Descendants<Row>().Select(r => (int)(r.RowIndex?.Value ?? 0)).ToList();
        Info("X5  row index gaps", $"rows present = [{string.Join(",", rowIdx)}] — an empty row is simply ABSENT; " +
            "the provider must count source row numbers from RowIndex, never from its own counter");
    }

    // ── Phase R — is a SAX read genuinely streaming? ─────────────────────────────────────────────────
    Section($"Phase R — streaming vs DOM on {BigRows:N0} rows");
    BuildBigWorkbook(BigPath, BigRows);
    Info("R0  big workbook", $"{BigPath} ({new FileInfo(BigPath).Length:N0} B)");

    long saxCells, domCells;
    double saxMb, domMb, saxSec, domSec;
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();
        saxCells = 0;
        long rows = 0;
        using (var doc = SpreadsheetDocument.Open(BigPath, false))
        {
            var wbPart = doc.WorkbookPart!;
            var wsPart = (WorksheetPart)wbPart.GetPartById(wbPart.Workbook.Sheets!.Elements<Sheet>().First().Id!.Value!);
            using var reader = OpenXmlReader.Create(wsPart);
            while (reader.Read())
            {
                if (reader.ElementType != typeof(Row)) continue;
                var row = (Row)reader.LoadCurrentElement()!;
                rows++;
                saxCells += row.Elements<Cell>().Count();
            }
        }
        sw.Stop();
        saxSec = sw.Elapsed.TotalSeconds;
        saxMb = (GC.GetTotalMemory(false) - before) / 1024.0 / 1024.0;
        Info("R1  SAX (OpenXmlReader, row by row)", $"{rows:N0} rows / {saxCells:N0} cells in {saxSec:N2}s — heap delta {saxMb:N1} MB");
    }
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();
        using (var doc = SpreadsheetDocument.Open(BigPath, false))
        {
            var wbPart = doc.WorkbookPart!;
            var wsPart = (WorksheetPart)wbPart.GetPartById(wbPart.Workbook.Sheets!.Elements<Sheet>().First().Id!.Value!);
            domCells = wsPart.Worksheet.Descendants<Cell>().LongCount();
        }
        sw.Stop();
        domSec = sw.Elapsed.TotalSeconds;
        domMb = (GC.GetTotalMemory(false) - before) / 1024.0 / 1024.0;
        Info("R2  DOM (Worksheet.Descendants)", $"{domCells:N0} cells in {domSec:N2}s — heap delta {domMb:N1} MB");
    }
    if (saxCells == domCells && saxMb > 0 && domMb > saxMb * 2)
        Pass("R3  streaming verdict", $"SAX reads the same {saxCells:N0} cells with {domMb / Math.Max(saxMb, 0.01):N1}x less heap — " +
            "the provider MUST use OpenXmlReader, not the DOM");
    else
        Info("R3  streaming verdict", $"SAX {saxMb:N1} MB / {saxSec:N2}s vs DOM {domMb:N1} MB / {domSec:N2}s (cells {saxCells:N0} vs {domCells:N0})");

    // ── Phase F — a workbook written by real Excel (structure only) ──────────────────────────────────
    Section("Phase F — a REAL Excel file (structure only, read-only)");
    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    var real = Path.Combine(desktop, "Fantomy - Technologie - Lista dla Streamsoft-1.xlsx");
    if (!File.Exists(real))
    {
        Info("F  real sample", $"not found at {real} — skipped");
    }
    else
    {
        using var doc = SpreadsheetDocument.Open(real, false);
        var wbPart = doc.WorkbookPart!;
        var sheets = wbPart.Workbook.Sheets!.Elements<Sheet>().ToList();
        Info("F1  sheets", string.Join(" · ", sheets.Select((s, i) => $"[{i}] {s.Name}")));

        var wsPart = (WorksheetPart)wbPart.GetPartById(sheets[0].Id!.Value!);
        Info("F2  SheetDimension", wsPart.Worksheet.SheetDimension?.Reference?.Value ?? "ABSENT");

        var shared = wbPart.SharedStringTablePart?.SharedStringTable;
        Info("F3  shared strings", shared is null ? "ABSENT" : $"{shared.Elements<SharedStringItem>().Count():N0} distinct items");

        var cellFormats = wbPart.WorkbookStylesPart?.Stylesheet?.CellFormats?.Elements<CellFormat>().ToList() ?? new List<CellFormat>();
        var customFormats = wbPart.WorkbookStylesPart?.Stylesheet?.NumberingFormats?.Elements<NumberingFormat>().ToList() ?? new List<NumberingFormat>();
        Info("F4  formats", $"{cellFormats.Count} xf entries; custom numFmts: " +
            (customFormats.Count == 0 ? "none" : string.Join(", ", customFormats.Take(8).Select(n => $"{n.NumberFormatId}='{n.FormatCode}'"))));

        // Per-column tally of cell kinds over the whole sheet, plus the shape of the first rows.
        var kindByCol = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        long totalRows = 0, totalCells = 0, dateCells = 0;
        var firstRowsShape = new List<string>();
        using (var reader = OpenXmlReader.Create(wsPart))
        {
            while (reader.Read())
            {
                if (reader.ElementType != typeof(Row)) continue;
                var row = (Row)reader.LoadCurrentElement()!;
                totalRows++;
                var cells = row.Elements<Cell>().ToList();
                totalCells += cells.Count;
                if (totalRows <= 4)
                    firstRowsShape.Add($"row {row.RowIndex?.Value}: " + string.Join(" ", cells.Select(c =>
                        $"{c.CellReference?.Value}={KindName(c)}")));
                foreach (var c in cells)
                {
                    var col = new string((c.CellReference?.Value ?? "?").TakeWhile(char.IsLetter).ToArray());
                    var kind = KindName(c);
                    uint styleIdx = c.StyleIndex?.Value ?? 0;
                    uint numFmtId = styleIdx < cellFormats.Count ? cellFormats[(int)styleIdx].NumberFormatId?.Value ?? 0u : 0u;
                    var fmtCode = customFormats.FirstOrDefault(n => n.NumberFormatId?.Value == numFmtId)?.FormatCode?.Value;
                    if (c.DataType?.Value is null && IsDateFormat(numFmtId, fmtCode)) { kind = "Number+dateFmt"; dateCells++; }
                    if (!kindByCol.TryGetValue(col, out var tally)) kindByCol[col] = tally = new Dictionary<string, int>(StringComparer.Ordinal);
                    tally[kind] = tally.GetValueOrDefault(kind) + 1;
                }
            }
        }
        Info("F5  size", $"{totalRows:N0} rows / {totalCells:N0} cells; cells that are numbers carrying a DATE format: {dateCells:N0}");
        foreach (var shape in firstRowsShape) Info("F6  first rows", shape);
        foreach (var (col, tally) in kindByCol.OrderBy(k => k.Key.Length).ThenBy(k => k.Key, StringComparer.Ordinal))
            Info($"F7  column {col}", string.Join(", ", tally.OrderByDescending(t => t.Value).Select(t => $"{t.Key}×{t.Value:N0}")));

        var ragged = totalRows > 0 && kindByCol.Count > 0 &&
                     kindByCol.Values.Select(t => t.Values.Sum()).Distinct().Count() > 1;
        if (ragged)
            Pass("F8  ragged rows", "columns do NOT all carry the same cell count — rows omit cells, confirming X4 on a real file");
        else
            Info("F8  ragged rows", "every column has the same cell count in this sample");
    }

    // ── Phase D2 — legacy .xls ───────────────────────────────────────────────────────────────────────
    Section("Phase D2 — does OpenXml read a legacy BIFF .xls?");
    var xls = Directory.Exists(desktop)
        ? Directory.GetFiles(desktop, "*.xls").FirstOrDefault()
        : null;
    if (xls is null)
    {
        Info("D2  legacy .xls", "no .xls sample found — premise unverified here (OpenXml is an OPC/ZIP reader by construction)");
    }
    else
    {
        try
        {
            using var doc = SpreadsheetDocument.Open(xls, false);
            var n = doc.WorkbookPart?.Workbook.Sheets?.Elements<Sheet>().Count() ?? 0;
            Fail("D2  legacy .xls", $"OpenXml OPENED {Path.GetFileName(xls)} ({n} sheets) — decision D2's premise is WRONG, re-examine it");
        }
        catch (Exception ex)
        {
            Pass("D2  legacy .xls", $"refused {Path.GetFileName(xls)} — {ex.GetType().Name}: {First(ex.Message)} ⇒ D2 confirmed: .xls needs a different library");
        }
    }
}
finally
{
    Section("Cleanup");
    foreach (var p in new[] { CasesPath, BigPath })
    {
        try { if (File.Exists(p)) File.Delete(p); Console.WriteLine($"  ....  deleted {p}"); }
        catch (Exception ex) { Console.WriteLine($"  ....  could not delete {p} — {ex.Message}"); }
    }
}

Console.WriteLine();
Console.WriteLine(failures == 0
    ? "ALL CHECKS OK (informational '....' lines carry the measurements)"
    : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

// ── helpers ─────────────────────────────────────────────────────────────────────────────────────────
static string First(string s)
{
    var line = s.Split('\n')[0].Trim();
    return line.Length > 140 ? line[..140] + "…" : line;
}

// In OpenXml 3.x CellValues is a struct whose ToString() does NOT yield the member name, so the raw
// DataType prints as "CellValues { }". Name it explicitly — the whole point of phases X/F is to report
// which KIND each cell is.
static string KindName(Cell c)
{
    if (c.DataType is null) return "Number/none";
    var v = c.DataType.Value;
    if (v == CellValues.SharedString) return "SharedString";
    if (v == CellValues.InlineString) return "InlineString";
    if (v == CellValues.String) return "String(formula)";
    if (v == CellValues.Boolean) return "Boolean";
    if (v == CellValues.Error) return "Error";
    if (v == CellValues.Date) return "Date(ISO)";
    if (v == CellValues.Number) return "Number(explicit)";
    return "Other";
}

// Built-in date/time number formats (14–22, 45–47) plus a custom code that mentions a date/time part.
// This is exactly the decision the provider will have to make, so the probe makes it the same way.
static bool IsDateFormat(uint numFmtId, string? formatCode)
{
    if (numFmtId is >= 14 and <= 22) return true;
    if (numFmtId is >= 45 and <= 47) return true;
    if (formatCode is null) return false;
    var code = formatCode.Replace("\"", "", StringComparison.Ordinal).ToLowerInvariant();
    return code.Contains('y') || code.Contains('d') || code.Contains('h') || code.Contains('s');
}

static void BuildCasesWorkbook(string path)
{
    if (File.Exists(path)) File.Delete(path);
    using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
    var wbPart = doc.AddWorkbookPart();
    wbPart.Workbook = new Workbook();

    var sst = wbPart.AddNewPart<SharedStringTablePart>();
    sst.SharedStringTable = new SharedStringTable(
        new SharedStringItem(new Text("Indeks kartoteki")),
        new SharedStringItem(new Text("Kod fantomu")),
        new SharedStringItem(new Text("GN-375-GTO-2KAB-EU")));

    var stylesPart = wbPart.AddNewPart<WorkbookStylesPart>();
    stylesPart.Stylesheet = new Stylesheet(
        new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "dd\\.mm\\.yyyy" }),
        new Fonts(new Font()),
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None })),
        new Borders(new Border()),
        new CellStyleFormats(new CellFormat()),
        new CellFormats(
            new CellFormat(),                                                        // 0 General
            new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true },        // 1 built-in date
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },       // 2 custom date
            new CellFormat { NumberFormatId = 2, ApplyNumberFormat = true }));       // 3 0.00

    var wsPart = wbPart.AddNewPart<WorksheetPart>();
    var sheetData = new SheetData();

    static Cell C(string reference, CellValues? type, string value, uint style = 0)
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

    // 1 — header: two shared strings + one inline string.
    sheetData.Append(new Row { RowIndex = 1U }.Append2(
        C("A1", CellValues.SharedString, "0"),
        C("B1", CellValues.SharedString, "1"),
        C("C1", CellValues.InlineString, "Nazwa fantomu")));
    // 2 — number, built-in date, custom date, boolean, shared string.
    sheetData.Append(new Row { RowIndex = 2U }.Append2(
        C("A2", null, "11881"),
        C("B2", null, "45000", 1),
        C("C2", null, "45000", 2),
        C("D2", CellValues.Boolean, "1"),
        C("E2", CellValues.SharedString, "2")));
    // 3 — THE TRAP: B is absent entirely.
    sheetData.Append(new Row { RowIndex = 3U }.Append2(
        C("A3", CellValues.InlineString, "przed luką"),
        C("C3", CellValues.InlineString, "po luce")));
    // 4 — a formula with a cached value.
    var formulaCell = new Cell { CellReference = "A4", CellFormula = new CellFormula("1+2"), CellValue = new CellValue("3") };
    sheetData.Append(new Row { RowIndex = 4U }.Append2(formulaCell));
    // 5 — an error cell.
    sheetData.Append(new Row { RowIndex = 5U }.Append2(C("A5", CellValues.Error, "#N/A")));
    // 6 — a long string.
    sheetData.Append(new Row { RowIndex = 6U }.Append2(C("A6", CellValues.InlineString, new string('D', 300))));
    // 7 — a decimal.
    sheetData.Append(new Row { RowIndex = 7U }.Append2(C("A7", null, "1234.56", 3)));
    // 10 — rows 8 and 9 are ABSENT (row-index gap).
    sheetData.Append(new Row { RowIndex = 10U }.Append2(C("A10", CellValues.InlineString, "po dwóch pustych wierszach")));

    wsPart.Worksheet = new Worksheet(sheetData);
    wbPart.Workbook.AppendChild(new Sheets(new Sheet
    {
        Id = wbPart.GetIdOfPart(wsPart),
        SheetId = 1U,
        Name = "Arkusz1",
    }));
    wbPart.Workbook.Save();
}

static void BuildBigWorkbook(string path, int rows)
{
    if (File.Exists(path)) File.Delete(path);
    using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
    var wbPart = doc.AddWorkbookPart();
    var wsPart = wbPart.AddNewPart<WorksheetPart>();

    using (var writer = OpenXmlWriter.Create(wsPart))
    {
        writer.WriteStartElement(new Worksheet());
        writer.WriteStartElement(new SheetData());
        for (int r = 1; r <= rows; r++)
        {
            writer.WriteStartElement(new Row { RowIndex = (uint)r });
            for (int c = 0; c < 5; c++)
            {
                var reference = $"{(char)('A' + c)}{r}";
                if (c == 0)
                {
                    writer.WriteStartElement(new Cell { CellReference = reference });
                    writer.WriteElement(new CellValue(r.ToString(CultureInfo.InvariantCulture)));
                }
                else
                {
                    writer.WriteStartElement(new Cell { CellReference = reference, DataType = CellValues.InlineString });
                    writer.WriteStartElement(new InlineString());
                    writer.WriteElement(new Text($"v{r}-{c}"));
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    using (var writer = OpenXmlWriter.Create(wbPart))
    {
        writer.WriteStartElement(new Workbook());
        writer.WriteStartElement(new Sheets());
        writer.WriteElement(new Sheet { Name = "Big", SheetId = 1U, Id = wbPart.GetIdOfPart(wsPart) });
        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}

internal static class RowExtensions
{
    // Row.Append(params OpenXmlElement[]) exists, but returning the row keeps the generator readable.
    public static Row Append2(this Row row, params Cell[] cells)
    {
        foreach (var c in cells) row.AppendChild(c);
        return row;
    }
}
