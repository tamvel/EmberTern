using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.Core.Export;

/// <summary>
/// CSV / TXT exporter — one engine, the delimiter is the only difference (CSV default <c>;</c> for
/// pl-PL, Text default TAB). Quoting is <b>RFC-4180</b>: a field is quoted iff it contains the
/// delimiter, a quote, CR, or LF, and internal quotes are doubled — so "separator inside data" is
/// safe and the file never corrupts. (This is deliberately NOT the clipboard's space-replacement —
/// see <see cref="ClipboardTextExporter"/>.) Line terminator is CRLF for Excel/Windows. Pure +
/// streaming: rows are written one at a time to the sink's writer.
/// </summary>
public sealed class DelimitedTextExporter : IExporter
{
    private const int ReportEvery = 1000;
    private const string LineTerminator = "\r\n";

    private readonly DelimitedTextOptions _options;
    private readonly CultureInfo _culture;

    public DelimitedTextExporter(DelimitedTextOptions options)
    {
        _options = options;
        _culture = options.UseInvariantCulture ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture;
    }

    public async Task<long> ExportAsync(
        IReadOnlyList<ExportColumn> columns,
        IAsyncEnumerable<object?[]> rows,
        IExportSink sink,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var writer = sink.Writer;

        if (_options.IncludeHeader)
        {
            var header = new List<string>(columns.Count);
            foreach (var c in columns) header.Add(c.Name);
            await writer.WriteAsync(BuildLine(header)).ConfigureAwait(false);
        }

        long written = 0;
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var fields = new List<string>(row.Length);
            foreach (var cell in row) fields.Add(ExportValueFormatter.Format(cell, _culture));
            await writer.WriteAsync(BuildLine(fields)).ConfigureAwait(false);

            written++;
            if (written % ReportEvery == 0) progress?.Report(written);
        }

        progress?.Report(written);
        return written;
    }

    private string BuildLine(IReadOnlyList<string> fields)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0) sb.Append(_options.Delimiter);
            sb.Append(QuoteField(fields[i]));
        }
        sb.Append(LineTerminator);
        return sb.ToString();
    }

    private string QuoteField(string field)
    {
        bool needsQuote = field.IndexOf(_options.Delimiter) >= 0
            || field.IndexOf('"') >= 0
            || field.IndexOf('\r') >= 0
            || field.IndexOf('\n') >= 0;

        if (!needsQuote) return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
