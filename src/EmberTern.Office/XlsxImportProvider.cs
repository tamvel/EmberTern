using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EmberTern.Core.Import;
using EmberTern.Core.Localization;

namespace EmberTern.Office;

/// <summary>
/// Reads an Excel <c>.xlsx</c> workbook into the pipeline's one currency: a <see cref="SourceSchema"/> plus a
/// stream of <see cref="RawRecord"/>.
/// <para>
/// ⭐ <b>This class is the whole of "EmberTern can import spreadsheets".</b> Nothing below
/// <see cref="IImportProvider"/> knows a workbook exists — mapping, conversion, validation, the writer and the
/// report are byte-for-byte the ones CSV already used (design §1.4). That is the pillar this etap exists to
/// test, and it held: the pipeline was not touched.
/// </para>
/// <para>
/// <b>Values leave here NATIVE</b> (<see cref="DateTime"/>, <see cref="double"/>, <see cref="bool"/>,
/// <see cref="string"/>), not as text. A workbook already knows a cell's type, and re-rendering it to text just
/// to parse it back under the user's culture settings would be a lossy round trip that could disagree with
/// itself. <c>ImportValueConverter</c> has carried a native branch since I2 for exactly this, so type inference
/// for a NEW table (etap I8) works on a sheet with no change at all.
/// </para>
/// <para>
/// The seven binding guidelines from I0's REK-6 are implemented here and each is marked at its site: SAX not DOM
/// (1), placement by <c>CellReference</c> (2), row numbers from <c>RowIndex</c> (3), dates only via the number
/// format (4), shared strings read once (5), the dimension as a hint only (6), cached formula values and error
/// cells as row errors (7).
/// </para>
/// </summary>
public sealed class XlsxImportProvider : IImportProvider
{
    /// <summary>How many worksheet rows the schema pass reads. Enough to see the widest row of a ragged sheet
    /// without paying for the whole workbook — mirrors the delimited provider's sample.</summary>
    public const int SchemaSampleRows = 200;

    public ImportProviderCapabilities Capabilities => ImportProviderCapabilities.Spreadsheet;

    /// <summary>
    /// The workbook's sheets, for the Format section's picker.
    /// <para>
    /// ⚠ <b>REK-6 (6): the dimension is a HINT.</b> I0 measured it present in Excel's own output
    /// (<c>A1:E8724</c>) and absent in programmatically written files, so a sheet whose dimension is missing
    /// simply reports <c>null</c> rows and the picker shows a name without a count. It is never used as truth —
    /// nothing decides how much to read from it.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<SourceSheet>> ListSheetsAsync(
        IImportSource source, CancellationToken cancellationToken)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        using var stream = await source.OpenStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = Open(stream, source);

        var workbookPart = document.WorkbookPart;
        if (workbookPart?.Workbook?.Sheets is null) return Array.Empty<SourceSheet>();

        var sheets = new List<SourceSheet>();
        var index = 0;
        foreach (var sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            long? rows = null;
            if (sheet.Id?.Value is { } relationshipId
                && workbookPart.GetPartById(relationshipId) is WorksheetPart part)
            {
                rows = RowsFromDimension(part);
            }

            sheets.Add(new SourceSheet(index++, sheet.Name?.Value ?? string.Empty, rows));
        }

        return sheets;
    }

    /// <summary>
    /// Describes the sheet's fields.
    /// <para>
    /// The width is the widest row in the sample, not the header's width — a sheet whose header row stops at D
    /// but whose data reaches F must still offer F as a mappable column, or data would be unreachable because a
    /// header forgot to name it. Same rule as the delimited provider, same reason.
    /// </para>
    /// </summary>
    public async Task<SourceSchema> ReadSchemaAsync(
        IImportSource source, ImportConfiguration configuration, CancellationToken cancellationToken)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var options = configuration.Spreadsheet ?? new SpreadsheetOptions();

        using var stream = await source.OpenStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = Open(stream, source);

        var workbookPart = document.WorkbookPart;
        var worksheetPart = ResolveWorksheet(workbookPart, options.SheetIndex);
        if (workbookPart is null || worksheetPart is null) return SourceSchema.Empty;

        var cells = new WorkbookCellReader(workbookPart, configuration);

        string?[] header = Array.Empty<string?>();
        var width = 0;
        var seen = 0;

        foreach (var row in StreamRows(worksheetPart, cancellationToken))
        {
            var rowIndex = (int)(row.RowIndex?.Value ?? 0);
            var values = cells.ReadRow(row);
            if (values.Length > width) width = values.Length;

            // The header is worksheet row 1, exactly as the delimited provider's header is record 1 — and for
            // the same reason FirstDataRow is a SEPARATE setting: a sheet may carry banner rows above its
            // header, and where the data starts is the user's statement, not a guess (§3.3).
            if (options.HasHeader && rowIndex == 1) header = values.Select(AsHeaderText).ToArray();

            if (++seen >= SchemaSampleRows) break;
        }

        if (width == 0) return SourceSchema.Empty;

        var fields = new List<SourceField>(width);
        for (var i = 0; i < width; i++)
        {
            var name = i < header.Length ? header[i]?.Trim() ?? string.Empty : string.Empty;
            var hasRealName = name.Length > 0;

            // An unnamed column keeps a usable key — and for a spreadsheet the positional label IS the column
            // letter the user sees in Excel, so A/B/C line up with the application's own preview.
            fields.Add(new SourceField(i, hasRealName ? name : SourceField.PositionalName(i), hasRealName));
        }

        return new SourceSchema(fields, options.HasHeader, RowsFromDimension(worksheetPart));
    }

    /// <summary>
    /// Streams the data rows inside the configured window.
    /// <para>
    /// ⭐ <b>REK-6 (3): the row number is the worksheet's own <c>RowIndex</c>.</b> I0 measured that empty rows
    /// are simply ABSENT from the file (rows <c>[1…7, 10]</c> for a sheet with 8 and 9 blank), so a running
    /// counter would make every later error point at the wrong row — a report that lies (§0.6).
    /// </para>
    /// </summary>
    public async IAsyncEnumerable<RawRecord> ReadRecordsAsync(
        IImportSource source,
        ImportConfiguration configuration,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var options = configuration.Spreadsheet ?? new SpreadsheetOptions();
        var first = Math.Max(1, options.FirstDataRow);
        var last = options.LastRow;

        using var stream = await source.OpenStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = Open(stream, source);

        var workbookPart = document.WorkbookPart;
        var worksheetPart = ResolveWorksheet(workbookPart, options.SheetIndex);
        if (workbookPart is null || worksheetPart is null) yield break;

        var cells = new WorkbookCellReader(workbookPart, configuration);

        foreach (var row in StreamRows(worksheetPart, cancellationToken))
        {
            var rowIndex = (int)(row.RowIndex?.Value ?? 0);
            if (rowIndex < first) continue;
            if (last is not null && rowIndex > last.Value) yield break;

            yield return new RawRecord(rowIndex, cells.ReadRow(row));
        }
    }

    /// <summary>
    /// ⭐ <b>REK-6 (1): SAX, never DOM.</b> I0 measured the same 500 000 cells at <b>3.9 MB</b> through
    /// <see cref="OpenXmlReader"/> against <b>300.5 MB</b> through <c>Descendants&lt;Cell&gt;()</c> — 77×, which
    /// on a million-row sheet is the difference between working and ~3 GB (design R8). Streaming is contractual
    /// here, not an optimisation, so this is the only way rows are ever read.
    /// <para>
    /// <c>LoadCurrentElement</c> materialises ONE row's subtree and lets it go; the workbook as a whole is never
    /// in memory.
    /// </para>
    /// </summary>
    private static IEnumerable<Row> StreamRows(WorksheetPart worksheetPart, CancellationToken cancellationToken)
    {
        using var reader = OpenXmlReader.Create(worksheetPart);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.ElementType != typeof(Row)) continue;
            if (reader.LoadCurrentElement() is Row row) yield return row;
        }
    }

    /// <summary>
    /// Opens the workbook, turning "this is not an OOXML package" into a sentence the user can act on.
    /// <para>
    /// ⚠ Found by running this provider over the machine's real spreadsheets: a file NAMED <c>.xlsx</c> is not
    /// necessarily one. An old workbook saved under the new extension is still BIFF, and
    /// <c>SpreadsheetDocument.Open</c> answers with <c>FileFormatException: File contains corrupted data</c> —
    /// which reads as "your file is damaged" when the true answer is "this is the old format" (findings §3.5).
    /// Saying so is the honest refusal §0 asks for; passing the raw message on is not.
    /// </para>
    /// <para>
    /// The advice changed in etap I10 and the reason is worth keeping: until then the old format could not be
    /// read at all, so the only way forward was Save As. <see cref="XlsImportProvider"/> reads it now, so the
    /// cheaper answer — rename the file to <c>.xls</c> — comes first. A refusal that still recommends the long
    /// way round after the short one exists is a message that has quietly stopped being true.
    /// </para>
    /// </summary>
    private static SpreadsheetDocument Open(Stream stream, IImportSource source)
    {
        try
        {
            return SpreadsheetDocument.Open(stream, isEditable: false);
        }
        catch (Exception ex) when (ex is FileFormatException or OpenXmlPackageException or InvalidDataException)
        {
            // ⭐ Etap C8 (D‑3): the sentence became a KEY plus its data, and the English form beside it is what
            // any catch-all reading `ex.Message` still gets. ⚠ The literal below is byte-identical to the one
            // this method threw before the migration, and the resource entry must render it exactly —
            // `ImportSourceLocalizationTests` compares the two on the real thrown exception, which is the only
            // machine check these words have.
            throw new ImportSourceException(
                LocalizableMessage.Of(ImportSourceMessages.NotReadableXlsx, source.DisplayName),
                $"'{source.DisplayName}' is not a readable .xlsx workbook. A file saved in the older Excel " +
                "format keeps working under an .xlsx name, but it cannot be read as one — rename it to .xls, " +
                "which EmberTern imports, or open it in Excel and use Save As.",
                ex);
        }
    }

    private static WorksheetPart? ResolveWorksheet(WorkbookPart? workbookPart, int sheetIndex)
    {
        var sheets = workbookPart?.Workbook?.Sheets?.Elements<Sheet>().ToList();
        if (sheets is null || sheets.Count == 0) return null;

        var index = sheetIndex >= 0 && sheetIndex < sheets.Count ? sheetIndex : 0;
        return sheets[index].Id?.Value is { } relationshipId
            ? workbookPart!.GetPartById(relationshipId) as WorksheetPart
            : null;
    }

    /// <summary>Row count from the sheet's declared dimension (<c>A1:E8724</c> → 8724), or <c>null</c>. A hint —
    /// see <see cref="ListSheetsAsync"/>.</summary>
    /// <summary>
    /// The sheet's declared row count, from <c>&lt;dimension&gt;</c> — a HINT only (REK-6 (6)).
    /// <para>
    /// ⭐⭐ <b>Read with the SAX reader, never through <c>worksheetPart.Worksheet</c>.</b> That property is the DOM
    /// accessor: touching it materializes the ENTIRE worksheet into an object tree, and it did so before even
    /// checking whether the element exists. Measured on a 300 000-row / 9,2 MB workbook
    /// (<c>tools/probes/ImportFileOpenProbe</c>): <b>8 546 ms through the DOM against 15 ms here</b>, for the same
    /// value (<c>A1:E300001</c>). Since this method is called once per sheet in <see cref="ListSheetsAsync"/> AND
    /// once at the end of <see cref="ReadSchemaAsync"/>, the workbook was being materialized twice per file
    /// selection — ~17 s of pure waste, on the UI thread, to read one attribute.
    /// </para>
    /// <para>
    /// ⚠ The stop at <c>&lt;sheetData&gt;</c> is what makes it cheap for a workbook that has NO dimension (some
    /// generators omit it): without it the reader would walk every row looking for an element that is not there,
    /// which would trade one expensive mechanism for another. <c>&lt;dimension&gt;</c> precedes
    /// <c>&lt;sheetData&gt;</c> in the schema, so reaching the latter proves the former is absent.
    /// Measured: 13 ms on the no-dimension file.
    /// </para>
    /// <para>
    /// ⛔ Do not "simplify" this back to the DOM property. It reads as the obvious one-liner, it returns the same
    /// answer, and it costs three orders of magnitude more — the class's own REK-6 (1) says SAX, not DOM, and this
    /// is the one place that had quietly broken that rule.
    /// </para>
    /// </summary>
    private static long? RowsFromDimension(WorksheetPart worksheetPart)
    {
        string? reference = null;
        using (var reader = OpenXmlReader.Create(worksheetPart))
        {
            while (reader.Read())
            {
                if (reader.ElementType == typeof(SheetDimension))
                {
                    reference = (reader.LoadCurrentElement() as SheetDimension)?.Reference?.Value;
                    break;
                }

                if (reader.ElementType == typeof(SheetData)) break;
            }
        }

        if (string.IsNullOrEmpty(reference)) return null;

        var end = reference.AsSpan(reference.IndexOf(':') + 1);
        var digits = end.TrimStart(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz$".AsSpan());

        return long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rows)
            ? rows
            : null;
    }

    private static string? AsHeaderText(object? value) => value switch
    {
        null => null,
        string s => s,
        // A header cell that is not text still names a column (a year used as a heading, say). Rendered
        // invariantly, because a header is an identifier, not a number to be formatted for a locale.
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
