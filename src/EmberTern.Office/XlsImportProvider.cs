using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Import;
using ExcelDataReader;

namespace EmberTern.Office;

/// <summary>
/// Reads a legacy Excel <c>.xls</c> (BIFF8) workbook into the pipeline's one currency: a
/// <see cref="SourceSchema"/> plus a stream of <see cref="RawRecord"/>.
/// <para>
/// ⭐ <b>The third implementation of <see cref="IImportProvider"/>, and again nothing below the port changed.</b>
/// Etap I9 proved the "one pipeline for every source" pillar (§1.4) by adding a workbook reader without touching
/// the pipeline, converter, validator, mapping planner or writer; I10 is the sharper test, because it also adds a
/// NuGet dependency — and the dependency reaches exactly one project. Everything past this class still believes
/// there is only one kind of source.
/// </para>
/// <para>
/// <b>Values leave here NATIVE</b> (<see cref="DateTime"/>, <see cref="double"/>, <see cref="bool"/>,
/// <see cref="string"/>, <see cref="TimeSpan"/>), exactly as they do from <see cref="XlsxImportProvider"/> — and
/// deliberately through the same <see cref="ExcelSerialDate"/> calendar and the same
/// <see cref="SpreadsheetNumberFormats"/> ruling on what counts as a date, so the two providers cannot disagree
/// about the same logical cell.
/// </para>
/// </summary>
public sealed class XlsImportProvider : IImportProvider
{
    /// <summary>How many worksheet rows the schema pass reads, mirroring <see cref="XlsxImportProvider"/>.</summary>
    public const int SchemaSampleRows = 200;

    static XlsImportProvider()
    {
        // BIFF8 stores text in a Windows code page, so without this a legacy workbook cannot be decoded at all.
        // EmberTern already registers the provider for Firebird's WIN1250 (CharsetCatalog), but that runs only
        // when that class is touched — and a provider must not depend on some other subsystem having been used
        // first. Registration is idempotent.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Sheets + a row range, no separators and no encoding choice — the same answer the <c>.xlsx</c>
    /// provider gives, because it is the same question. The Format section reads this rather than the source
    /// kind, which is why a second spreadsheet format needed no XAML at all (§3.3).</summary>
    public ImportProviderCapabilities Capabilities => ImportProviderCapabilities.Spreadsheet;

    /// <summary>
    /// The workbook's sheets, for the Format section's picker.
    /// <para>
    /// Unlike <c>.xlsx</c> — where the row count comes from a declared dimension that is only a hint and is often
    /// absent — BIFF's reader states <see cref="IExcelDataReader.RowCount"/> for every sheet, so the picker can
    /// show a real count here.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<SourceSheet>> ListSheetsAsync(
        IImportSource source, CancellationToken cancellationToken)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        using var stream = await source.OpenStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = Open(stream, source);

        var sheets = new List<SourceSheet>();
        var index = 0;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            sheets.Add(new SourceSheet(index++, reader.Name ?? string.Empty, reader.RowCount));
        }
        while (reader.NextResult());

        return sheets;
    }

    /// <summary>
    /// Describes the sheet's fields.
    /// <para>
    /// The width is the reader's own <see cref="IDataRecord.FieldCount"/> for the sheet — BIFF declares it, so
    /// unlike the <c>.xlsx</c> path there is no need to infer it from the widest sampled row. A header row that
    /// stops short of that width therefore still leaves the remaining columns mappable, which is the same
    /// guarantee, reached more directly: data must never be unreachable because a header forgot to name it.
    /// </para>
    /// </summary>
    public async Task<SourceSchema> ReadSchemaAsync(
        IImportSource source, ImportConfiguration configuration, CancellationToken cancellationToken)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var options = configuration.Spreadsheet ?? new SpreadsheetOptions();

        using var stream = await source.OpenStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = Open(stream, source);
        if (!SelectSheet(reader, options.SheetIndex)) return SourceSchema.Empty;

        var cells = new XlsCellReader(configuration);
        var width = reader.FieldCount;
        var header = Array.Empty<object?>();

        var seen = 0;
        while (reader.Read() && seen < SchemaSampleRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The header is worksheet row 1, exactly as it is for .xlsx and for a delimited file — and
            // FirstDataRow stays a SEPARATE setting, because a sheet may carry banner rows above its header and
            // where the data starts is the user's statement, not a guess (§3.3).
            if (options.HasHeader && SourceRowNumber(reader) == 1) header = cells.ReadRow(reader);

            seen++;
        }

        if (width == 0) return SourceSchema.Empty;

        var fields = new List<SourceField>(width);
        for (var i = 0; i < width; i++)
        {
            var name = i < header.Length ? AsHeaderText(header[i])?.Trim() ?? string.Empty : string.Empty;
            var hasRealName = name.Length > 0;

            // An unnamed column keeps a usable key, and for a spreadsheet the positional label IS the column
            // letter the user sees in Excel, so A/B/C line up with the application's own preview.
            fields.Add(new SourceField(i, hasRealName ? name : SourceField.PositionalName(i), hasRealName));
        }

        return new SourceSchema(fields, options.HasHeader, reader.RowCount > 0 ? reader.RowCount : null);
    }

    /// <summary>
    /// Streams the data rows inside the configured window.
    /// <para>
    /// ⭐ <b>The row number is the sheet's own, and this format reaches that differently from <c>.xlsx</c>.</b>
    /// I0 measured that an OOXML sheet simply OMITS an empty row, so <c>.xlsx</c> has to read the row's declared
    /// index. Measured for BIFF (I10): a blank row is <b>yielded</b>, and <see cref="IDataReader.Depth"/> stays
    /// aligned with the sheet's own 0-based row index across the gap. So the number is <c>Depth + 1</c> — the
    /// reader's own statement of where it is, never a counter of our own. Same §0.6 rule, different container:
    /// carrying the <c>.xlsx</c> reasoning over without measuring would have been a guess.
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
        using var reader = Open(stream, source);
        if (!SelectSheet(reader, options.SheetIndex)) yield break;

        var cells = new XlsCellReader(configuration);

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rowNumber = SourceRowNumber(reader);
            if (rowNumber < first) continue;
            if (last is not null && rowNumber > last.Value) yield break;

            yield return new RawRecord(rowNumber, cells.ReadRow(reader));
        }
    }

    /// <summary>The sheet's own 1-based row number — see <see cref="ReadRecordsAsync"/> for why it is
    /// <c>Depth + 1</c> and why that had to be measured rather than assumed.</summary>
    private static int SourceRowNumber(IExcelDataReader reader) => reader.Depth + 1;

    /// <summary>
    /// Opens the workbook, turning "this is not a BIFF workbook" into a sentence the user can act on.
    /// <para>
    /// ⚠ The mirror of <see cref="XlsxImportProvider"/>'s refusal, and measured the same way: handed a real OOXML
    /// package, the binary reader answers <c>Invalid file signature</c>, and handed an OLE2 container that is not
    /// a workbook at all it answers <c>Neither stream 'Workbook' nor 'Book' was found</c>. Neither reads as
    /// anything a user could act on, so both become the one sentence that names the actual situation.
    /// </para>
    /// <para>
    /// It opens the BINARY reader specifically rather than the library's auto-detecting one. Auto-detection would
    /// quietly make this class a second reader for <c>.xlsx</c> as well, and two owners of one format is how they
    /// drift apart; a file that is not what its extension claims deserves an answer, not a silent rescue.
    /// </para>
    /// </summary>
    private static IExcelDataReader Open(Stream stream, IImportSource source)
    {
        try
        {
            return ExcelReaderFactory.CreateBinaryReader(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException(
                $"'{source.DisplayName}' is not a readable .xls workbook. A file saved in the newer Excel " +
                "format keeps working under an .xls name, but it cannot be read as one — rename it to .xlsx, " +
                "or open it in Excel and use Save As.",
                ex);
        }
    }

    /// <summary>Positions the reader on the requested sheet. An index outside the workbook falls back to the
    /// first sheet, matching <see cref="XlsxImportProvider"/> — a stored configuration naming a sheet that is
    /// gone must not throw from inside a read (§4.8.5).</summary>
    private static bool SelectSheet(IExcelDataReader reader, int sheetIndex)
    {
        if (reader.ResultsCount == 0) return false;

        var index = sheetIndex >= 0 && sheetIndex < reader.ResultsCount ? sheetIndex : 0;
        for (var i = 0; i < index; i++)
        {
            if (!reader.NextResult()) return false;
        }
        return true;
    }

    private static string? AsHeaderText(object? value) => value switch
    {
        null => null,
        string s => s,
        // A header cell that is not text still names a column (a year used as a heading, say). Rendered
        // invariantly, because a header is an identifier, not a number to be formatted for a locale.
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
